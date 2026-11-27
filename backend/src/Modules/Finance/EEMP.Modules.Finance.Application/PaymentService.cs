using EEMP.Modules.Finance.Domain;
using EEMP.Modules.People.Contracts;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Paging;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Finance.Application.Fmt;

namespace EEMP.Modules.Finance.Application;

/// <summary>Receipts and allocation (DL-060, DL-061), shared by manual and online payments. Works inside the caller's unit of work.</summary>
public sealed class PaymentPosting(IFinanceDbContext db, FinanceActor actor, FinanceAuditor audit, DocumentNumberer numbers)
{
    /// <summary>Marks the payment succeeded, takes the next receipt number and allocates it.</summary>
    public async Task SucceedAsync(Payment p, CancellationToken ct)
    {
        if (p.Status == PaymentStatus.Succeeded) return;
        p.Status = PaymentStatus.Succeeded;
        p.SucceededAt = actor.UtcNow;
        p.FailureCode = null;
        p.ReceiptNumber = await numbers.NextAsync(DocumentNumbers.Receipt, ct);
        audit.Write("payment.succeeded", "payment", p.Id, p.TargetInvoiceId, p.Id, p.StudentId, p.Amount, p.Currency, $"{Camel(p.Method)};{p.ReceiptNumber}");
        await AllocateAsync(p, p.TargetInvoiceId, p.TargetInstallmentId, ct);
    }

    /// <summary>Allocates the payment's unallocated amount to one invoice or to the student's open invoices by due date.</summary>
    public async Task AllocateAsync(Payment p, Guid? invoiceId, Guid? installmentId, CancellationToken ct)
    {
        if (p.Unallocated <= 0) return;
        var q = db.Invoices.Include(i => i.Installments)
            .Where(i => i.StudentId == p.StudentId && (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid));
        if (invoiceId is { } only) q = q.Where(i => i.Id == only);
        var invoices = (await q.ToListAsync(ct)).Where(i => i.Currency == p.Currency)
            .OrderBy(i => i.DueDate ?? DateOnly.MaxValue).ThenBy(i => i.IssueDate).ThenBy(i => i.Number, StringComparer.Ordinal).ToList();
        var targets = invoices.Select(i => new AllocationTarget(i.Id, i.Balance,
            i.Installments.Select(x => (x.Id, x.DueDate, x.Remaining)).ToList())).ToList();
        var (lines, _) = Allocator.Allocate(p.Unallocated, targets, installmentId);
        foreach (var line in lines)
        {
            var inv = invoices.First(i => i.Id == line.InvoiceId);
            db.Allocations.Add(new PaymentAllocation { PaymentId = p.Id, InvoiceId = inv.Id, InstallmentId = line.InstallmentId, Amount = line.Amount, CreatedAt = actor.UtcNow });
            inv.PaidAmount += line.Amount;
            if (line.InstallmentId is { } instId) inv.Installments.First(x => x.Id == instId).PaidAmount += line.Amount;
            p.Allocated += line.Amount;
        }
        foreach (var inv in invoices.Where(i => lines.Any(l => l.InvoiceId == i.Id)))
        {
            InvoiceCalculator.UpdatePaymentStatus(inv);
            audit.Write("payment.allocated", "invoice", inv.Id, inv.Id, p.Id, p.StudentId, lines.Where(l => l.InvoiceId == inv.Id).Sum(l => l.Amount), p.Currency);
        }
    }
}

public sealed class PaymentMapper(IFinanceDbContext db, StudentNames names)
{
    public async Task<IReadOnlyList<PaymentDto>> ToDtosAsync(IReadOnlyList<Payment> payments, CancellationToken ct)
    {
        var students = await names.GetAsync(payments.Select(p => p.StudentId), ct);
        var invoiceIds = payments.SelectMany(p => p.Allocations).Select(a => a.InvoiceId).Distinct().ToList();
        var numbers = await db.Invoices.AsNoTracking().Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Number, ct);
        return payments.Select(p =>
        {
            var s = students.GetValueOrDefault(p.StudentId);
            return new PaymentDto(p.Id, p.StudentId, s is null ? "" : $"{s.FirstName} {s.LastName}".Trim(), p.BranchId, Camel(p.Method), Camel(p.Status),
                p.Amount, p.Currency, Date(p.ReceivedOn), p.Reference,
                p.Method == PaymentMethod.Cheque ? new ChequeDto(p.ChequeNumber, p.ChequeBank, Date(p.ChequeDueDate), Camel(p.ChequeStatus)) : null,
                p.Provider, p.ProviderRef, p.ReceiptNumber, p.Allocated, p.Unallocated, p.Refunded, p.FailureCode,
                p.Allocations.OrderBy(a => a.CreatedAt).Select(a => new AllocationDto(a.InvoiceId, numbers.GetValueOrDefault(a.InvoiceId), a.InstallmentId, a.Amount)).ToList(),
                p.CreatedAt, Rv(p.RowVersion));
        }).ToList();
    }

    public async Task<PaymentDto> ToDtoAsync(Payment p, CancellationToken ct) => (await ToDtosAsync([p], ct))[0];
}

public sealed class PaymentService(IFinanceDbContext db, FinanceActor actor, FinanceAccess access, FinanceAuditor audit, FinanceUnitOfWork uow,
    PaymentPosting posting, PaymentMapper mapper, IPeopleDirectory people, IFinanceApprovals approvals)
{
    public async Task<PagedResult<PaymentDto>> ListAsync(PageRequest page, PaymentListQuery query, CancellationToken ct)
    {
        var scope = await access.ScopeAsync(Permissions.FinanceRead, ct);
        var q = db.Payments.AsNoTracking().Include(p => p.Allocations).AsQueryable();
        if (!scope.AllBranches) { var ids = scope.BranchIds.ToList(); q = q.Where(p => ids.Contains(p.BranchId)); }
        var v = new Validator();
        if (ParseEnum<PaymentMethod>(query.Method, "method", v) is { } m) q = q.Where(p => p.Method == m);
        if (ParseEnum<PaymentStatus>(query.Status, "status", v) is { } st) q = q.Where(p => p.Status == st);
        var from = OptionalDate(query.From, "from", v);
        var to = OptionalDate(query.To, "to", v);
        v.ThrowIfInvalid();
        if (from is { } f) q = q.Where(p => p.ReceivedOn >= f);
        if (to is { } t) q = q.Where(p => p.ReceivedOn <= t);
        if (query.StudentId is { } sid) q = q.Where(p => p.StudentId == sid);
        if (query.BranchId is { } bid) q = q.Where(p => p.BranchId == bid);
        if (Clean(page.Search) is { } search) q = q.Where(p => (p.ReceiptNumber != null && p.ReceiptNumber.Contains(search)) || (p.Reference != null && p.Reference.Contains(search)));
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(p => p.CreatedAt).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<PaymentDto>(await mapper.ToDtosAsync(rows, ct), page.Page, page.PageSize, total);
    }

    public async Task<PaymentDto> GetAsync(Guid id, CancellationToken ct)
    {
        var p = await db.Payments.AsNoTracking().Include(x => x.Allocations).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        await access.EnsureAsync(p.BranchId, Permissions.FinanceRead, ct);
        return await mapper.ToDtoAsync(p, ct);
    }

    public async Task<(PaymentDto Payment, bool Created)> RecordAsync(PaymentRequest req, CancellationToken ct)
    {
        var currency = await actor.CurrencyAsync(ct);
        var v = new Validator().When(req.StudentId is null || req.StudentId == Guid.Empty, "studentId", Validator.Required);
        var method = ParseEnum<PaymentMethod>(req.Method, "method", v, required: true);
        v.When(method == PaymentMethod.Online, "method", Validator.InvalidValue);
        var amount = Amount(req.Amount, "amount", currency, v);
        var receivedOn = OptionalDate(req.ReceivedOn, "receivedOn", v);
        v.OptionalText("reference", req.Reference, 100).OptionalText("idempotencyKey", req.IdempotencyKey, 100);
        DateOnly? chequeDue = null;
        if (method == PaymentMethod.Cheque)
        {
            v.When(req.Cheque is null, "cheque", Validator.Required);
            v.RequiredText("cheque.number", req.Cheque?.Number, 50).RequiredText("cheque.bank", req.Cheque?.Bank, 100);
            chequeDue = OptionalDate(req.Cheque?.DueDate, "cheque.dueDate", v);
        }
        v.ThrowIfInvalid();
        var student = (await people.GetStudentsAsync([req.StudentId!.Value], ct)).GetValueOrDefault(req.StudentId.Value) ?? throw new NotFoundException();
        await access.EnsureAsync(student.BranchId, Permissions.FinancePaymentsManage, ct);
        var key = Clean(req.IdempotencyKey);
        var today = await actor.TodayAsync(ct);
        if (receivedOn > today) new Validator().Add("receivedOn", Validator.InvalidValue).ThrowIfInvalid();

        return await uow.RunAsync(async () =>
        {
            if (key is not null && await db.Payments.Include(p => p.Allocations).FirstOrDefaultAsync(p => p.IdempotencyKey == key, ct) is { } existing)
            {
                if (existing.StudentId != student.Id || existing.Amount != amount) throw new ConflictException(ErrorCodes.DuplicateValue, P(("field", "idempotencyKey")));
                return (await mapper.ToDtoAsync(existing, ct), false);
            }
            if (req.InvoiceId is { } invId)
            {
                var inv = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invId, ct);
                if (inv is null || inv.StudentId != student.Id) new Validator().Add("invoiceId", Validator.InvalidValue).ThrowIfInvalid();
                if (!InvoiceStateMachine.CanReceivePayment(inv!.Status)) throw new ConflictException(FinanceErrors.InvalidState, P(("status", Camel(inv.Status))));
            }
            var p = new Payment
            {
                StudentId = student.Id, BranchId = student.BranchId, Method = method!.Value, Amount = amount!.Value, Currency = currency,
                ReceivedOn = receivedOn ?? today, Reference = Clean(req.Reference), TargetInvoiceId = req.InvoiceId, IdempotencyKey = key, CreatedBy = actor.UserId,
            };
            if (method == PaymentMethod.Cheque)
            {
                p.ChequeNumber = req.Cheque!.Number!.Trim();
                p.ChequeBank = req.Cheque.Bank!.Trim();
                p.ChequeDueDate = chequeDue;
                p.ChequeStatus = ChequeStatus.Received;
            }
            db.Payments.Add(p);
            audit.Write("payment.recorded", "payment", p.Id, p.TargetInvoiceId, p.Id, p.StudentId, p.Amount, p.Currency, Camel(p.Method));
            if (method != PaymentMethod.Cheque) await posting.SucceedAsync(p, ct);
            await db.SaveChangesAsync(ct);
            return (await mapper.ToDtoAsync(p, ct), true);
        }, ct);
    }

    public Task<PaymentDto> SetChequeStatusAsync(Guid id, ChequeStatusRequest req, CancellationToken ct) => uow.RunAsync(async () =>
    {
        var v = new Validator();
        var status = ParseEnum<ChequeStatus>(req.Status, "status", v, required: true);
        v.When(status == ChequeStatus.Received, "status", Validator.InvalidValue);
        v.ThrowIfInvalid();
        var p = await db.Payments.Include(x => x.Allocations).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        await access.EnsureAsync(p.BranchId, Permissions.FinancePaymentsManage, ct);
        CheckRowVersion(p, req.RowVersion);
        if (p.Method != PaymentMethod.Cheque || p.Status != PaymentStatus.Pending) throw new ConflictException(FinanceErrors.InvalidState, P(("status", Camel(p.Status))));
        p.ChequeStatus = status;
        if (status == ChequeStatus.Cleared)
        {
            if (p.TargetInvoiceId is { } t && !await db.Invoices.AnyAsync(i => i.Id == t && (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid), ct))
                p.TargetInvoiceId = null; // target settled or voided meanwhile: allocate by due date instead
            await posting.SucceedAsync(p, ct);
        }
        else
        {
            p.Status = PaymentStatus.Failed;
            p.FailureCode = "cheque_bounced";
            audit.Write("payment.chequeBounced", "payment", p.Id, p.TargetInvoiceId, p.Id, p.StudentId, p.Amount, p.Currency);
        }
        await db.SaveChangesAsync(ct);
        return await mapper.ToDtoAsync(p, ct);
    }, ct);

    public async Task<InvoiceDto> ApplyCreditAsync(Guid studentId, ApplyCreditRequest req, InvoiceMapper invoices, CancellationToken ct)
    {
        new Validator().When(req.InvoiceId is null || req.InvoiceId == Guid.Empty, "invoiceId", Validator.Required).ThrowIfInvalid();
        return await uow.RunAsync(async () =>
        {
            var inv = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == req.InvoiceId && i.StudentId == studentId, ct) ?? throw new NotFoundException();
            await access.EnsureAsync(inv.BranchId, Permissions.FinanceInvoicesManage, ct);
            if (!InvoiceStateMachine.CanReceivePayment(inv.Status)) throw new ConflictException(FinanceErrors.InvalidState, P(("status", Camel(inv.Status))));
            var payments = (await db.Payments.Where(p => p.StudentId == studentId && p.Status == PaymentStatus.Succeeded && p.Currency == inv.Currency).ToListAsync(ct))
                .Where(p => p.Unallocated > 0).OrderBy(p => p.SucceededAt).ToList();
            if (payments.Count == 0) throw new BusinessRuleException(FinanceErrors.NoCredit);
            foreach (var p in payments)
            {
                await posting.AllocateAsync(p, inv.Id, null, ct);
                audit.Write("credit.applied", "payment", p.Id, inv.Id, p.Id, studentId, details: "see payment.allocated");
            }
            await db.SaveChangesAsync(ct);
            var fresh = await InvoiceMapper.WithDetails(db.Invoices.AsNoTracking()).FirstAsync(i => i.Id == inv.Id, ct);
            return await invoices.ToDtoAsync(fresh, await actor.TodayAsync(ct), ct);
        }, ct);
    }

    public async Task<StudentAccountDto> AccountAsync(Guid studentId, CancellationToken ct)
    {
        var student = (await people.GetStudentsAsync([studentId], ct)).GetValueOrDefault(studentId) ?? throw new NotFoundException();
        var scope = await access.ScopeAsync(Permissions.FinanceRead, ct);
        var invoices = (await db.Invoices.AsNoTracking().Include(i => i.Installments).Where(i => i.StudentId == studentId).ToListAsync(ct))
            .Where(i => scope.Includes(i.BranchId)).ToList();
        var payments = (await db.Payments.AsNoTracking().Include(p => p.Allocations).Where(p => p.StudentId == studentId).ToListAsync(ct))
            .Where(p => scope.Includes(p.BranchId)).ToList();
        if (!scope.Includes(student.BranchId) && invoices.Count == 0 && payments.Count == 0) throw new NotFoundException();
        return await BuildAccountAsync(student, invoices, payments, includeDrafts: true, ct);
    }

    public async Task<StudentAccountDto> BuildAccountAsync(StudentSummary student, IReadOnlyList<Invoice> invoices, IReadOnlyList<Payment> payments, bool includeDrafts, CancellationToken ct)
    {
        var today = await actor.TodayAsync(ct);
        var currency = await actor.CurrencyAsync(ct);
        var visible = invoices.Where(i => includeDrafts || i.Status != InvoiceStatus.Draft).OrderByDescending(i => i.CreatedAt).ToList();
        var counted = visible.Where(i => i.Status is not (InvoiceStatus.Draft or InvoiceStatus.Void)).ToList();
        var names = new Dictionary<Guid, StudentSummary> { [student.Id] = student };
        return new StudentAccountDto(student.Id, $"{student.FirstName} {student.LastName}".Trim(), student.StudentNumber, currency,
            counted.Sum(i => i.Total + i.LateFeeTotal - i.CreditTotal), counted.Sum(i => i.PaidAmount), counted.Sum(i => i.Balance),
            payments.Sum(p => p.Unallocated), counted.Where(i => InvoiceCalculator.IsOverdue(i, today)).Sum(i => i.Balance),
            visible.Select(i => InvoiceMapper.ToSummary(i, names, today)).ToList(),
            await mapper.ToDtosAsync(payments.OrderByDescending(p => p.CreatedAt).ToList(), ct));
    }

    // Refunds (maker side) -------------------------------------------------------------------------------------------------

    public Task<RefundDto> RequestRefundAsync(Guid paymentId, RefundRequest req, CancellationToken ct) => uow.RunAsync(async () =>
    {
        var p = await db.Payments.FirstOrDefaultAsync(x => x.Id == paymentId, ct) ?? throw new NotFoundException();
        await access.EnsureAsync(p.BranchId, Permissions.FinancePaymentsManage, ct);
        var v = new Validator().RequiredText("reason", req.Reason, 500);
        var amount = Amount(req.Amount, "amount", p.Currency, v);
        v.ThrowIfInvalid();
        if (p.Status != PaymentStatus.Succeeded) throw new ConflictException(FinanceErrors.InvalidState, P(("status", Camel(p.Status))));
        var pending = await db.Refunds.Where(r => r.PaymentId == p.Id && r.Status == ApprovalStatus.Pending).SumAsync(r => (decimal?)r.Amount, ct) ?? 0;
        if (amount > p.Unallocated - pending) throw new BusinessRuleException(FinanceErrors.RefundExceedsCredit, P(("available", p.Unallocated - pending)));
        var r = new Refund { PaymentId = p.Id, StudentId = p.StudentId, BranchId = p.BranchId, Amount = amount!.Value, Currency = p.Currency,
            Reason = req.Reason!.Trim(), RequestedBy = actor.UserId, RequestedAt = actor.UtcNow };
        db.Refunds.Add(r);
        p.UpdatedAt = actor.UtcNow; // serializes concurrent refund requests against the same credit
        audit.Write("refund.requested", "refund", r.Id, null, p.Id, p.StudentId, r.Amount, r.Currency);
        await approvals.SubmitAsync(new ApprovalSubject("refund", r.Id, r.BranchId, r.StudentId, r.Amount, r.Currency, r.RequestedBy), ct);
        await db.SaveChangesAsync(ct);
        return ApprovalService.ToDto(r);
    }, ct);

    public async Task<IReadOnlyList<RefundDto>> ListRefundsAsync(string? status, CancellationToken ct)
    {
        var scope = await access.ScopeAsync(Permissions.FinanceRead, ct);
        var v = new Validator();
        var st = ParseEnum<ApprovalStatus>(status, "status", v);
        v.ThrowIfInvalid();
        var q = db.Refunds.AsNoTracking();
        if (st is { } s) q = q.Where(r => r.Status == s);
        return (await q.OrderByDescending(r => r.RequestedAt).Take(500).ToListAsync(ct)).Where(r => scope.Includes(r.BranchId)).Select(ApprovalService.ToDto).ToList();
    }
}
