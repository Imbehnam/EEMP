using EEMP.Modules.Finance.Domain;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Tenancy;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Finance.Application.Fmt;

namespace EEMP.Modules.Finance.Application;

public sealed record GatewayRequest(Guid PaymentId, decimal Amount, string Currency, string CallbackUrl, string Description);
public sealed record GatewayRequestResult(bool Success, string? Authority, string? Error);
/// <param name="PaidAmount">Amount the provider reports as paid, when it reports one (checked against the stored amount).</param>
public sealed record GatewayVerifyResult(bool Success, string? ProviderRef, decimal? PaidAmount, string? Error);

/// <summary>A payment provider (DL-066). Implementations never see student data; only amount, currency and our payment id.</summary>
public interface IPaymentGateway
{
    string Name { get; }
    bool Enabled { get; }
    Task<GatewayRequestResult> RequestAsync(GatewayRequest request, CancellationToken ct);
    string PaymentUrl(string authority);
    Task<GatewayVerifyResult> VerifyAsync(string authority, decimal amount, string currency, CancellationToken ct);
}

public sealed class OnlinePaymentService(IFinanceDbContext db, FinanceActor actor, FinanceAuditor audit, FinanceUnitOfWork uow, PaymentPosting posting,
    FinanceSetupService setup, IEnumerable<IPaymentGateway> gateways, ITenantContext tenant)
{
    private IPaymentGateway? Gateway(string? name = null) =>
        gateways.Where(g => g.Enabled && (name is null || string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(g => g.Name == "fake" ? 1 : 0).FirstOrDefault();

    /// <summary>Portal: starts an online payment for the caller's own or linked student's open invoice. The amount is the server's.</summary>
    public async Task<PayOnlineResult> InitiateAsync(Guid invoiceId, PayOnlineRequest req, string callbackBase, CancellationToken ct)
    {
        var inv = await db.Invoices.AsNoTracking().Include(i => i.Installments).FirstOrDefaultAsync(i => i.Id == invoiceId, ct) ?? throw new NotFoundException();
        if (inv.Status == InvoiceStatus.Draft || !(await actor.PortalStudentIdsAsync(ct)).Contains(inv.StudentId)) throw new NotFoundException();
        var v = new Validator().RequiredText("idempotencyKey", req.IdempotencyKey, 100);
        var partial = Amount(req.Amount, "amount", inv.Currency, v, required: false);
        v.ThrowIfInvalid();
        var key = req.IdempotencyKey!.Trim();
        var settings = await setup.LoadAsync(ct);
        var gateway = settings.OnlinePaymentsEnabled ? Gateway() : null;
        if (gateway is null) throw new BusinessRuleException(FinanceErrors.OnlinePaymentsDisabled);

        if (await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.IdempotencyKey == key, ct) is { } existing)
        {
            if (existing.TargetInvoiceId != inv.Id || existing.Method != PaymentMethod.Online) throw new ConflictException(ErrorCodes.DuplicateValue, P(("field", "idempotencyKey")));
            if (existing.ProviderAuthority is null || existing.Status == PaymentStatus.Failed) throw new BusinessRuleException(FinanceErrors.GatewayUnavailable);
            var g = Gateway(existing.Provider) ?? throw new BusinessRuleException(FinanceErrors.GatewayUnavailable);
            return new PayOnlineResult(existing.Id, g.PaymentUrl(existing.ProviderAuthority), g.Name);
        }

        if (!InvoiceStateMachine.CanReceivePayment(inv.Status)) throw new ConflictException(FinanceErrors.InvalidState, P(("status", Camel(inv.Status))));
        var outstanding = inv.Balance;
        if (req.InstallmentId is { } instId)
        {
            var inst = inv.Installments.FirstOrDefault(i => i.Id == instId);
            if (inst is null) new Validator().Add("installmentId", Validator.InvalidValue).ThrowIfInvalid();
            outstanding = Math.Min(inst!.Remaining, inv.Balance);
        }
        if (outstanding <= 0) throw new ConflictException(FinanceErrors.InvalidState, P(("status", Camel(inv.Status))));
        if (partial is { } part && part > outstanding) throw new BusinessRuleException(FinanceErrors.AmountExceedsBalance, P(("balance", outstanding)));
        var amount = partial ?? outstanding;

        var payment = new Payment
        {
            StudentId = inv.StudentId, BranchId = inv.BranchId, Method = PaymentMethod.Online, Amount = amount, Currency = inv.Currency,
            ReceivedOn = await actor.TodayAsync(ct), Provider = gateway.Name, TargetInvoiceId = inv.Id, TargetInstallmentId = req.InstallmentId,
            IdempotencyKey = key, CreatedBy = actor.UserId,
        };
        db.Payments.Add(payment);
        audit.Write("payment.online.started", "payment", payment.Id, inv.Id, payment.Id, inv.StudentId, amount, inv.Currency, gateway.Name);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ResetTracking(); // same idempotency key raced: answer with the winner
            return await InitiateAsync(invoiceId, req, callbackBase, ct);
        }

        var callback = $"{callbackBase.TrimEnd('/')}/api/v1/finance/gateway/{gateway.Name}/callback/{payment.Id}";
        GatewayRequestResult result;
        try
        {
            result = await gateway.RequestAsync(new GatewayRequest(payment.Id, amount, inv.Currency, callback, $"Invoice {inv.Number}"), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            result = new GatewayRequestResult(false, null, "unreachable");
        }
        if (!result.Success || string.IsNullOrEmpty(result.Authority))
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailureCode = "gateway_request_failed";
            audit.Write("payment.online.requestFailed", "payment", payment.Id, inv.Id, payment.Id, inv.StudentId, amount, inv.Currency, result.Error);
            await db.SaveChangesAsync(ct);
            throw new BusinessRuleException(FinanceErrors.GatewayUnavailable);
        }
        payment.ProviderAuthority = result.Authority;
        await db.SaveChangesAsync(ct);
        return new PayOnlineResult(payment.Id, gateway.PaymentUrl(result.Authority), gateway.Name);
    }

    /// <summary>
    /// Anonymous provider callback. The payment id in the URL and the authority stored at request time bind the callback to one
    /// tenant, invoice and amount; verification always uses the stored amount. Replays return the stored outcome.
    /// </summary>
    public async Task<PaymentStatusDto> CallbackAsync(string provider, Guid paymentId, string? authority, string? status, CancellationToken ct)
    {
        var located = await db.Payments.IgnoreQueryFilters().AsNoTracking().Where(p => p.Id == paymentId)
            .Select(p => new { p.TenantId, p.Provider, p.ProviderAuthority }).FirstOrDefaultAsync(ct);
        if (located is null || !string.Equals(located.Provider, provider, StringComparison.OrdinalIgnoreCase) || located.ProviderAuthority is null
            || !string.Equals(located.ProviderAuthority, authority, StringComparison.Ordinal))
            throw new NotFoundException();
        using var _ = tenant.BeginTenantScope(located.TenantId);
        return await SettleAsync(paymentId, status, ct);
    }

    /// <summary>Portal: re-check a pending online payment of the caller's own/linked student (idempotent).</summary>
    public async Task<PaymentStatusDto> VerifyAsync(Guid paymentId, CancellationToken ct)
    {
        var p = await db.Payments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == paymentId, ct) ?? throw new NotFoundException();
        if (p.Method != PaymentMethod.Online || !(await actor.PortalStudentIdsAsync(ct)).Contains(p.StudentId)) throw new NotFoundException();
        if (p.ProviderAuthority is null) return Status(p);
        return await SettleAsync(paymentId, "OK", ct);
    }

    private static PaymentStatusDto Status(Payment p) => new(p.Id, Camel(p.Status), p.ReceiptNumber, p.FailureCode);

    private Task<PaymentStatusDto> SettleAsync(Guid paymentId, string? status, CancellationToken ct) => uow.RunAsync(async () =>
    {
        var p = await db.Payments.Include(x => x.Allocations).FirstAsync(x => x.Id == paymentId, ct);
        if (p.Status != PaymentStatus.Pending) return Status(p);
        if (!string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
        {
            p.Status = PaymentStatus.Cancelled;
            p.FailureCode = "cancelled_by_payer";
            audit.Write("payment.online.cancelled", "payment", p.Id, p.TargetInvoiceId, p.Id, p.StudentId, p.Amount, p.Currency);
            await db.SaveChangesAsync(ct);
            return Status(p);
        }
        var gateway = Gateway(p.Provider) ?? throw new BusinessRuleException(FinanceErrors.GatewayUnavailable);
        GatewayVerifyResult result;
        try
        {
            result = await gateway.VerifyAsync(p.ProviderAuthority!, p.Amount, p.Currency, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new BusinessRuleException(FinanceErrors.GatewayUnavailable); // stays pending; a later callback/verify settles it
        }
        if (!result.Success)
        {
            p.Status = PaymentStatus.Failed;
            p.FailureCode = FinanceErrors.PaymentFailed;
            audit.Write("payment.online.failed", "payment", p.Id, p.TargetInvoiceId, p.Id, p.StudentId, p.Amount, p.Currency, result.Error);
        }
        else if (result.PaidAmount is { } paid && paid != p.Amount)
        {
            p.Status = PaymentStatus.Failed;
            p.FailureCode = FinanceErrors.AmountMismatch;
            p.ProviderRef = result.ProviderRef;
            audit.Write("payment.online.amountMismatch", "payment", p.Id, p.TargetInvoiceId, p.Id, p.StudentId, paid, p.Currency, $"expected={p.Amount}");
        }
        else
        {
            p.ProviderRef = result.ProviderRef;
            if (p.TargetInvoiceId is { } t && !await db.Invoices.AnyAsync(i => i.Id == t && (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid), ct))
                p.TargetInvoiceId = null; // invoice settled/voided meanwhile: the money becomes credit or pays the next invoice
            await posting.SucceedAsync(p, ct);
        }
        await db.SaveChangesAsync(ct);
        return Status(p);
    }, ct);
}
