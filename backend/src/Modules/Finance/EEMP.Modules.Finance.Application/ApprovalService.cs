using EEMP.Modules.Finance.Domain;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Finance.Application.Fmt;

namespace EEMP.Modules.Finance.Application;

/// <summary>
/// The approval seam (DL-063). M3 implements it locally (maker-checker inside Finance); EEMP-011 can later supply an implementation
/// that routes the decision through the workflow engine and calls back into <see cref="ApprovalService"/> to apply it.
/// </summary>
public interface IFinanceApprovals
{
    /// <summary>Throws unless the current user may decide a request made by <paramref name="requestedBy"/> in <paramref name="branchId"/>.</summary>
    Task EnsureCanDecideAsync(Guid branchId, Guid? requestedBy, CancellationToken ct);
}

public sealed class LocalMakerChecker(FinanceAccess access, FinanceActor actor) : IFinanceApprovals
{
    public async Task EnsureCanDecideAsync(Guid branchId, Guid? requestedBy, CancellationToken ct)
    {
        await access.EnsureAsync(branchId, Permissions.FinanceApprove, ct);
        if (requestedBy is not null && requestedBy == actor.UserId) throw new ForbiddenException(FinanceErrors.MakerChecker);
    }
}

public sealed class ApprovalService(IFinanceDbContext db, FinanceActor actor, FinanceAccess access, FinanceAuditor audit, FinanceUnitOfWork uow,
    IFinanceApprovals approvals, StudentNames names)
{
    public static RefundDto ToDto(Refund r) => new(r.Id, r.PaymentId, r.StudentId, r.BranchId, r.Amount, r.Currency, r.Reason, Camel(r.Status),
        r.RequestedBy, r.RequestedAt, r.DecidedBy, r.DecidedAt, r.DecisionNote, Rv(r.RowVersion));

    public async Task<IReadOnlyList<ApprovalItemDto>> ListAsync(string? status, CancellationToken ct)
    {
        var v = new Validator();
        var st = ParseEnum<ApprovalStatus>(status ?? "pending", "status", v);
        v.ThrowIfInvalid();
        var scope = await access.ScopeAsync(Permissions.FinanceApprove, ct);
        var readScope = await access.ScopeAsync(Permissions.FinanceRead, ct);
        var discounts = await (from d in db.Discounts.AsNoTracking()
                               join i in db.Invoices.AsNoTracking() on d.InvoiceId equals i.Id
                               where d.Status == st
                               orderby d.RequestedAt
                               select new { d, i.StudentId, i.BranchId, i.Currency }).Take(500).ToListAsync(ct);
        var refunds = await db.Refunds.AsNoTracking().Where(r => r.Status == st).OrderBy(r => r.RequestedAt).Take(500).ToListAsync(ct);
        bool Visible(Guid b) => scope.Includes(b) || readScope.Includes(b);
        var students = await names.GetAsync(discounts.Select(d => d.StudentId).Concat(refunds.Select(r => r.StudentId)), ct);
        string Name(Guid id) => students.GetValueOrDefault(id) is { } s ? $"{s.FirstName} {s.LastName}".Trim() : "";
        var items = discounts.Where(x => Visible(x.BranchId)).Select(x => new ApprovalItemDto("discount", x.d.Id, x.d.InvoiceId, null, x.StudentId, Name(x.StudentId),
                x.BranchId, x.d.Value, x.Currency, $"{Camel(x.d.Kind)}:{Camel(x.d.Type)}", x.d.Reason, x.d.RequestedBy, x.d.RequestedAt, Rv(x.d.RowVersion)))
            .Concat(refunds.Where(r => Visible(r.BranchId)).Select(r => new ApprovalItemDto("refund", r.Id, null, r.PaymentId, r.StudentId, Name(r.StudentId), r.BranchId,
                r.Amount, r.Currency, "refund", r.Reason, r.RequestedBy, r.RequestedAt, Rv(r.RowVersion))))
            .OrderBy(i => i.RequestedAt).ToList();
        return items;
    }

    public Task<DiscountDto> DecideDiscountAsync(Guid id, bool approve, DecisionRequest req, CancellationToken ct) => uow.RunAsync(async () =>
    {
        new Validator().OptionalText("note", req.Note, 500).ThrowIfInvalid();
        var d = await db.Discounts.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        var inv = await InvoiceMapper.WithDetails(db.Invoices).FirstAsync(i => i.Id == d.InvoiceId, ct);
        await approvals.EnsureCanDecideAsync(inv.BranchId, d.RequestedBy, ct);
        CheckRowVersion(d, req.RowVersion);
        if (d.Status != ApprovalStatus.Pending) throw new ConflictException(FinanceErrors.InvalidState, P(("status", Camel(d.Status))));
        if (inv.Status != InvoiceStatus.Draft) throw new ConflictException(FinanceErrors.InvalidState, P(("status", Camel(inv.Status))));
        d.Status = approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        d.DecidedBy = actor.UserId;
        d.DecidedAt = actor.UtcNow;
        d.DecisionNote = Clean(req.Note);
        InvoiceCalculator.Recalculate(inv);
        inv.UpdatedAt = actor.UtcNow;
        audit.Write(approve ? "discount.approved" : "discount.rejected", "discount", d.Id, inv.Id, studentId: inv.StudentId, amount: d.Amount,
            currency: inv.Currency, details: $"requestedBy={d.RequestedBy}");
        await db.SaveChangesAsync(ct);
        return InvoiceMapper.ToDto(d);
    }, ct);

    public async Task CancelDiscountAsync(Guid id, CancellationToken ct)
    {
        var d = await db.Discounts.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        var inv = await db.Invoices.FirstAsync(i => i.Id == d.InvoiceId, ct);
        if (d.RequestedBy == actor.UserId) await access.EnsureAsync(inv.BranchId, Permissions.FinanceInvoicesManage, ct);
        else await access.EnsureAsync(inv.BranchId, Permissions.FinanceApprove, ct);
        if (d.Status != ApprovalStatus.Pending) throw new ConflictException(FinanceErrors.InvalidState, P(("status", Camel(d.Status))));
        db.Discounts.Remove(d);
        inv.UpdatedAt = actor.UtcNow;
        audit.Write("discount.cancelled", "discount", d.Id, inv.Id, studentId: inv.StudentId, amount: d.Value, currency: inv.Currency);
        await db.SaveChangesAsync(ct);
    }

    public Task<RefundDto> DecideRefundAsync(Guid id, bool approve, DecisionRequest req, CancellationToken ct) => uow.RunAsync(async () =>
    {
        new Validator().OptionalText("note", req.Note, 500).ThrowIfInvalid();
        var r = await db.Refunds.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        await approvals.EnsureCanDecideAsync(r.BranchId, r.RequestedBy, ct);
        CheckRowVersion(r, req.RowVersion);
        if (r.Status != ApprovalStatus.Pending) throw new ConflictException(FinanceErrors.InvalidState, P(("status", Camel(r.Status))));
        var p = await db.Payments.FirstAsync(x => x.Id == r.PaymentId, ct);
        if (approve)
        {
            if (r.Amount > p.Unallocated) throw new BusinessRuleException(FinanceErrors.RefundExceedsCredit, P(("available", p.Unallocated)));
            p.Refunded += r.Amount;
        }
        r.Status = approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        r.DecidedBy = actor.UserId;
        r.DecidedAt = actor.UtcNow;
        r.DecisionNote = Clean(req.Note);
        p.UpdatedAt = actor.UtcNow;
        audit.Write(approve ? "refund.approved" : "refund.rejected", "refund", r.Id, null, p.Id, r.StudentId, r.Amount, r.Currency, $"requestedBy={r.RequestedBy}");
        await db.SaveChangesAsync(ct);
        return ToDto(r);
    }, ct);
}
