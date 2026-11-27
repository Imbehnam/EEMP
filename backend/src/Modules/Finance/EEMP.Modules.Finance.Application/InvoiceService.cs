using EEMP.Modules.Academic.Contracts;
using EEMP.Modules.Finance.Domain;
using EEMP.Modules.People.Contracts;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Paging;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Finance.Application.Fmt;

namespace EEMP.Modules.Finance.Application;

/// <summary>Invoice mapping shared by the admin, portal and report services.</summary>
public sealed class InvoiceMapper(StudentNames names)
{
    public static IQueryable<Invoice> WithDetails(IQueryable<Invoice> q) =>
        q.Include(i => i.Lines).Include(i => i.Discounts).Include(i => i.Installments).Include(i => i.Adjustments);

    public static string InstallmentStatus(Invoice inv, Installment i, DateOnly today) =>
        inv.Status == InvoiceStatus.Paid || i.Remaining <= 0 ? "paid" : inv.IsOpen && i.DueDate < today ? "overdue" : "open";

    public async Task<InvoiceDto> ToDtoAsync(Invoice inv, DateOnly today, CancellationToken ct)
    {
        var students = await names.GetAsync([inv.StudentId], ct);
        return ToDto(inv, students, today);
    }

    public static InvoiceDto ToDto(Invoice inv, IReadOnlyDictionary<Guid, StudentSummary> students, DateOnly today)
    {
        var s = students.GetValueOrDefault(inv.StudentId);
        return new InvoiceDto(inv.Id, inv.Number, inv.StudentId, s is null ? "" : $"{s.FirstName} {s.LastName}".Trim(), s?.StudentNumber ?? "",
            inv.BranchId, inv.TermId, Camel(inv.Status), inv.Currency, Date(inv.IssueDate), Date(inv.DueDate), inv.Subtotal, inv.DiscountTotal,
            inv.TaxRatePercent, inv.TaxTotal, inv.LateFeeTotal, inv.CreditTotal, inv.Total, inv.PaidAmount, inv.Balance,
            InvoiceCalculator.IsOverdue(inv, today), inv.Note, inv.VoidReason, inv.IssuedAt, inv.VoidedAt,
            inv.Lines.OrderBy(l => l.SortOrder).Select(l => new InvoiceLineDto(l.Id, l.FeeItemId, l.EnrollmentId, l.Description, l.Quantity, l.UnitPrice, l.Amount, l.Taxable)).ToList(),
            inv.Discounts.OrderBy(d => d.RequestedAt).Select(ToDto).ToList(),
            inv.Installments.OrderBy(i => i.SeqNo).Select(i => new InstallmentDto(i.Id, i.SeqNo, Date(i.DueDate), i.Amount, i.PaidAmount, i.LateFeeCharged,
                InstallmentStatus(inv, i, today))).ToList(),
            inv.Adjustments.OrderBy(a => a.CreatedAt).Select(a => new AdjustmentDto(a.Id, Camel(a.Kind), a.Number, a.Amount, a.Reason, a.InstallmentId, a.CreatedAt)).ToList(),
            Rv(inv.RowVersion));
    }

    public static InvoiceSummaryDto ToSummary(Invoice inv, IReadOnlyDictionary<Guid, StudentSummary> students, DateOnly today)
    {
        var s = students.GetValueOrDefault(inv.StudentId);
        return new InvoiceSummaryDto(inv.Id, inv.Number, inv.StudentId, s is null ? "" : $"{s.FirstName} {s.LastName}".Trim(), s?.StudentNumber ?? "",
            inv.BranchId, inv.TermId, Camel(inv.Status), inv.Currency, Date(inv.IssueDate), Date(inv.DueDate), inv.Total, inv.PaidAmount, inv.Balance,
            InvoiceCalculator.IsOverdue(inv, today), Rv(inv.RowVersion));
    }

    public static DiscountDto ToDto(InvoiceDiscount d) => new(d.Id, d.InvoiceId, Camel(d.Kind), Camel(d.Type), d.Value, d.Amount, d.Reason, Camel(d.Status),
        d.RequestedBy, d.RequestedAt, d.DecidedBy, d.DecidedAt, d.DecisionNote, Rv(d.RowVersion), d.WorkflowInstanceId);
}

public sealed class InvoiceService(IFinanceDbContext db, FinanceActor actor, FinanceAccess access, FinanceAuditor audit, FinanceUnitOfWork uow,
    DocumentNumberer numbers, InvoiceMapper mapper, StudentNames names, IPeopleDirectory people, IEnrollmentDirectory academic,
    FinanceSetupService setup, IFinanceApprovals approvals)
{
    public const int MaxBulkEnrollments = 500;

    // Reads ---------------------------------------------------------------------------------------------------------------

    public async Task<PagedResult<InvoiceSummaryDto>> ListAsync(PageRequest page, InvoiceListQuery query, CancellationToken ct)
    {
        var scope = await access.ScopeAsync(Permissions.FinanceRead, ct);
        var q = db.Invoices.AsNoTracking().Include(i => i.Installments).AsQueryable();
        if (!scope.AllBranches) { var ids = scope.BranchIds.ToList(); q = q.Where(i => ids.Contains(i.BranchId)); }
        var v = new Validator();
        if (ParseEnum<InvoiceStatus>(query.Status, "status", v) is { } st) q = q.Where(i => i.Status == st);
        v.ThrowIfInvalid();
        if (query.StudentId is { } sid) q = q.Where(i => i.StudentId == sid);
        if (query.BranchId is { } bid) q = q.Where(i => i.BranchId == bid);
        if (query.TermId is { } tid) q = q.Where(i => i.TermId == tid);
        if (Clean(page.Search) is { } search) q = q.Where(i => i.Number != null && i.Number.Contains(search));
        var today = await actor.TodayAsync(ct);
        List<Invoice> rows;
        int total;
        if (query.OverdueOnly == true)
        {
            var open = (await q.Where(i => i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid).ToListAsync(ct))
                .Where(i => InvoiceCalculator.IsOverdue(i, today)).OrderBy(i => i.DueDate).ToList();
            total = open.Count;
            rows = open.Skip(page.Skip).Take(page.PageSize).ToList();
        }
        else
        {
            total = await q.CountAsync(ct);
            rows = await q.OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Number).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        }
        var students = await names.GetAsync(rows.Select(r => r.StudentId), ct);
        return new PagedResult<InvoiceSummaryDto>(rows.Select(r => InvoiceMapper.ToSummary(r, students, today)).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<InvoiceDto> GetAsync(Guid id, CancellationToken ct)
    {
        var inv = await LoadAsync(id, false, ct);
        await access.EnsureAsync(inv.BranchId, Permissions.FinanceRead, ct);
        return await mapper.ToDtoAsync(inv, await actor.TodayAsync(ct), ct);
    }

    private async Task<Invoice> LoadAsync(Guid id, bool track, CancellationToken ct)
    {
        var q = InvoiceMapper.WithDetails(db.Invoices);
        if (!track) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException();
    }

    private async Task<Invoice> LoadForWriteAsync(Guid id, string? rowVersion, CancellationToken ct, string permission = Permissions.FinanceInvoicesManage)
    {
        var inv = await LoadAsync(id, true, ct);
        await access.EnsureAsync(inv.BranchId, permission, ct);
        if (rowVersion is not null || permission == Permissions.FinanceInvoicesManage) CheckRowVersion(inv, rowVersion);
        return inv;
    }

    // Drafts ---------------------------------------------------------------------------------------------------------------

    public async Task<InvoiceDto> CreateAsync(InvoiceRequest req, CancellationToken ct)
    {
        var v = new Validator().When(req.StudentId is null || req.StudentId == Guid.Empty, "studentId", Validator.Required);
        v.ThrowIfInvalid();
        var student = (await people.GetStudentsAsync([req.StudentId!.Value], ct)).GetValueOrDefault(req.StudentId.Value);
        if (student is null) throw new NotFoundException();
        await access.EnsureAsync(student.BranchId, Permissions.FinanceInvoicesManage, ct);
        if (req.TermId is { } term && !await academic.TermExistsAsync(term, ct)) new Validator().Add("termId", Validator.InvalidValue).ThrowIfInvalid();
        var currency = await actor.CurrencyAsync(ct);
        var inv = new Invoice { StudentId = student.Id, BranchId = student.BranchId, TermId = req.TermId, Currency = currency, CreatedBy = actor.UserId };
        await ApplyDraftAsync(inv, req, ct);
        db.Invoices.Add(inv);
        audit.Write("invoice.created", "invoice", inv.Id, inv.Id, studentId: inv.StudentId, amount: inv.Total, currency: inv.Currency);
        await db.SaveChangesAsync(ct);
        return await mapper.ToDtoAsync(inv, await actor.TodayAsync(ct), ct);
    }

    public async Task<InvoiceDto> UpdateAsync(Guid id, InvoiceRequest req, CancellationToken ct)
    {
        var inv = await LoadForWriteAsync(id, req.RowVersion, ct);
        if (!InvoiceStateMachine.CanEdit(inv.Status)) throw new ConflictException(FinanceErrors.InvalidState, P(("status", Camel(inv.Status))));
        if (req.TermId is { } term && !await academic.TermExistsAsync(term, ct)) new Validator().Add("termId", Validator.InvalidValue).ThrowIfInvalid();
        inv.TermId = req.TermId ?? inv.TermId;
        await ApplyDraftAsync(inv, req, ct);
        audit.Write("invoice.updated", "invoice", inv.Id, inv.Id, studentId: inv.StudentId, amount: inv.Total, currency: inv.Currency);
        await db.SaveChangesAsync(ct);
        return await mapper.ToDtoAsync(inv, await actor.TodayAsync(ct), ct);
    }

    private async Task ApplyDraftAsync(Invoice inv, InvoiceRequest req, CancellationToken ct)
    {
        var v = new Validator();
        var due = OptionalDate(req.DueDate, "dueDate", v);
        v.OptionalText("note", req.Note, 1000);
        var lines = req.Lines ?? [];
        v.When(lines.Count > 100, "lines", Validator.InvalidValue);
        var feeIds = lines.Where(l => l.FeeItemId is not null).Select(l => l.FeeItemId!.Value).Distinct().ToList();
        var fees = await db.FeeItems.AsNoTracking().Where(f => feeIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
        var typeIds = fees.Values.Select(f => f.FeeTypeId).Distinct().ToList();
        var taxable = await db.FeeTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Taxable, ct);
        var built = new List<InvoiceLine>();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var qty = l.Quantity ?? 1;
            v.When(qty is < 1 or > 1000, $"lines[{i}].quantity", Validator.InvalidValue);
            if (l.FeeItemId is { } fid)
            {
                if (!fees.TryGetValue(fid, out var fee)) { v.Add($"lines[{i}].feeItemId", Validator.InvalidValue); continue; }
                if (fee.Currency != inv.Currency) throw new BusinessRuleException(FinanceErrors.CurrencyMismatch);
                if (!fee.IsActive) throw new BusinessRuleException(FinanceErrors.FeeItemInactive);
                built.Add(new InvoiceLine { FeeItemId = fid, Description = Clean(l.Description) ?? fee.Name, Quantity = qty, UnitPrice = fee.Amount,
                    Taxable = taxable.GetValueOrDefault(fee.FeeTypeId), SortOrder = i });
            }
            else
            {
                v.RequiredText($"lines[{i}].description", l.Description, 200);
                var price = Amount(l.UnitPrice, $"lines[{i}].unitPrice", inv.Currency, v);
                if (price is not null && !string.IsNullOrWhiteSpace(l.Description))
                    built.Add(new InvoiceLine { Description = l.Description.Trim(), Quantity = qty, UnitPrice = price.Value, SortOrder = i });
            }
            if (Clean(l.Description) is { Length: > 200 }) v.Add($"lines[{i}].description", Validator.MaxLength);
        }
        v.ThrowIfInvalid();
        // Keep generated (enrollment) lines; replace manual lines with the request's.
        inv.Lines.RemoveAll(l => l.EnrollmentId is null);
        var offset = inv.Lines.Count;
        foreach (var line in built) { line.InvoiceId = inv.Id; line.SortOrder += offset; inv.Lines.Add(line); }
        inv.DueDate = due ?? inv.DueDate;
        inv.Note = Clean(req.Note);
        InvoiceCalculator.Recalculate(inv);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var inv = await LoadAsync(id, true, ct);
        await access.EnsureAsync(inv.BranchId, Permissions.FinanceInvoicesManage, ct);
        if (!InvoiceStateMachine.CanEdit(inv.Status)) throw new ConflictException(FinanceErrors.InvalidState, P(("status", Camel(inv.Status))));
        db.GeneratedCharges.RemoveRange(await db.GeneratedCharges.Where(c => c.InvoiceId == id).ToListAsync(ct));
        db.Invoices.Remove(inv);
        audit.Write("invoice.deleted", "invoice", inv.Id, inv.Id, studentId: inv.StudentId, amount: inv.Total, currency: inv.Currency);
        await db.SaveChangesAsync(ct);
    }

    // Lifecycle ------------------------------------------------------------------------------------------------------------

    public Task<InvoiceDto> IssueAsync(Guid id, IssueRequest req, CancellationToken ct) => uow.RunAsync(async () =>
    {
        var v = new Validator();
        var issueDate = OptionalDate(req.IssueDate, "issueDate", v);
        var dueDate = OptionalDate(req.DueDate, "dueDate", v);
        v.ThrowIfInvalid();
        var inv = await LoadForWriteAsync(id, req.RowVersion, ct);
        if (!InvoiceStateMachine.CanIssue(inv.Status)) throw new ConflictException(FinanceErrors.InvalidState, P(("status", Camel(inv.Status))));
        if (inv.Discounts.Any(d => d.IsOpen)) throw new BusinessRuleException(FinanceErrors.DiscountPending);
        var settings = await setup.LoadAsync(ct);
        inv.TaxRatePercent = settings.VatEnabled ? settings.VatRatePercent : 0;
        InvoiceCalculator.Recalculate(inv);
        if (inv.Total <= 0) throw new BusinessRuleException(FinanceErrors.InvoiceEmpty);
        if (inv.Installments.Count > 0 && inv.Installments.Sum(i => i.Amount) != inv.Total)
            throw new BusinessRuleException(FinanceErrors.InstallmentsMismatch, P(("total", inv.Total)));
        var today = await actor.TodayAsync(ct);
        inv.IssueDate = issueDate ?? today;
        inv.DueDate = dueDate ?? inv.DueDate ?? inv.Installments.OrderBy(i => i.DueDate).Select(i => (DateOnly?)i.DueDate).LastOrDefault() ?? inv.IssueDate;
        inv.Number = await numbers.NextAsync(DocumentNumbers.Invoice, ct);
        inv.Status = InvoiceStatus.Issued;
        inv.IssuedAt = actor.UtcNow;
        audit.Write("invoice.issued", "invoice", inv.Id, inv.Id, studentId: inv.StudentId, amount: inv.Total, currency: inv.Currency, details: inv.Number);
        await db.SaveChangesAsync(ct);
        return await mapper.ToDtoAsync(inv, today, ct);
    }, ct);

    public Task<InvoiceDto> VoidAsync(Guid id, VoidRequest req, CancellationToken ct) => uow.RunAsync(async () =>
    {
        new Validator().RequiredText("reason", req.Reason, 500).ThrowIfInvalid();
        var inv = await LoadForWriteAsync(id, req.RowVersion, ct);
        if (!InvoiceStateMachine.CanVoid(inv.Status)) throw new ConflictException(FinanceErrors.InvalidState, P(("status", Camel(inv.Status))));
        var allocations = await db.Allocations.Where(a => a.InvoiceId == id).ToListAsync(ct);
        var paymentIds = allocations.Select(a => a.PaymentId).Distinct().ToList();
        var payments = await db.Payments.Where(p => paymentIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        foreach (var a in allocations)
        {
            payments[a.PaymentId].Allocated -= a.Amount;
            audit.Write("allocation.released", "payment", a.PaymentId, inv.Id, a.PaymentId, inv.StudentId, a.Amount, inv.Currency);
        }
        db.Allocations.RemoveRange(allocations);
        foreach (var i in inv.Installments) i.PaidAmount = 0;
        inv.PaidAmount = 0;
        db.GeneratedCharges.RemoveRange(await db.GeneratedCharges.Where(c => c.InvoiceId == id).ToListAsync(ct));
        inv.Status = InvoiceStatus.Void;
        inv.VoidReason = req.Reason!.Trim();
        inv.VoidedAt = actor.UtcNow;
        audit.Write("invoice.voided", "invoice", inv.Id, inv.Id, studentId: inv.StudentId, amount: inv.Total, currency: inv.Currency, details: inv.VoidReason);
        await db.SaveChangesAsync(ct);
        return await mapper.ToDtoAsync(inv, await actor.TodayAsync(ct), ct);
    }, ct);

    public Task<InvoiceDto> CreditNoteAsync(Guid id, CreditNoteRequest req, CancellationToken ct) => uow.RunAsync(async () =>
    {
        var inv = await LoadForWriteAsync(id, req.RowVersion, ct);
        var v = new Validator().RequiredText("reason", req.Reason, 500);
        var amount = Amount(req.Amount, "amount", inv.Currency, v);
        v.ThrowIfInvalid();
        if (!InvoiceStateMachine.CanCredit(inv.Status)) throw new ConflictException(FinanceErrors.InvalidState, P(("status", Camel(inv.Status))));
        if (amount > inv.Balance) throw new BusinessRuleException(FinanceErrors.AmountExceedsBalance, P(("balance", inv.Balance)));
        var number = await numbers.NextAsync(DocumentNumbers.CreditNote, ct);
        var adj = new InvoiceAdjustment { InvoiceId = inv.Id, Kind = AdjustmentKind.CreditNote, Number = number, Amount = amount!.Value,
            Reason = req.Reason!.Trim(), CreatedAt = actor.UtcNow, CreatedBy = actor.UserId };
        inv.Adjustments.Add(adj);
        inv.CreditTotal += adj.Amount;
        InvoiceCalculator.UpdatePaymentStatus(inv);
        audit.Write("invoice.credited", "creditNote", adj.Id, inv.Id, studentId: inv.StudentId, amount: adj.Amount, currency: inv.Currency, details: number);
        await db.SaveChangesAsync(ct);
        return await mapper.ToDtoAsync(inv, await actor.TodayAsync(ct), ct);
    }, ct);

    // Generation -----------------------------------------------------------------------------------------------------------

    public async Task<GenerateResult> GenerateAsync(GenerateRequest req, CancellationToken ct)
    {
        var v = new Validator().When(req.EnrollmentId is null || req.EnrollmentId == Guid.Empty, "enrollmentId", Validator.Required);
        var due = OptionalDate(req.DueDate, "dueDate", v);
        v.ThrowIfInvalid();
        var enrollment = await academic.GetAsync(req.EnrollmentId!.Value, ct) ?? throw new NotFoundException();
        await access.EnsureAsync(enrollment.BranchId, Permissions.FinanceInvoicesManage, ct);
        var fees = await FeeItemsAsync(req.FeeItemIds, ct);
        return await GenerateForAsync([enrollment], fees, due, ct);
    }

    public async Task<GenerateResult> GenerateBulkAsync(GenerateBulkRequest req, CancellationToken ct)
    {
        var v = new Validator().When((req.ClassId is null) == (req.TermId is null), "classId", Validator.Required);
        var due = OptionalDate(req.DueDate, "dueDate", v);
        v.ThrowIfInvalid();
        IReadOnlyList<EnrollmentInfo> enrollments;
        if (req.ClassId is { } classId)
        {
            var cls = await academic.GetClassAsync(classId, ct) ?? throw new NotFoundException();
            await access.EnsureAsync(cls.BranchId, Permissions.FinanceInvoicesManage, ct);
            enrollments = await academic.ListForClassAsync(classId, ct);
        }
        else
        {
            if (!await academic.TermExistsAsync(req.TermId!.Value, ct)) throw new NotFoundException();
            var scope = await access.ScopeAsync(Permissions.FinanceInvoicesManage, ct);
            if (scope.IsEmpty) throw new ForbiddenException();
            enrollments = (await academic.ListForTermAsync(req.TermId.Value, ct)).Where(e => scope.Includes(e.BranchId)).ToList();
        }
        var fees = await FeeItemsAsync(req.FeeItemIds, ct);
        if (enrollments.Count > MaxBulkEnrollments) throw new BusinessRuleException(FinanceErrors.InvalidState, P(("max", MaxBulkEnrollments)));
        return await GenerateForAsync(enrollments, fees, due, ct);
    }

    private async Task<IReadOnlyList<FeeItem>> FeeItemsAsync(IReadOnlyList<Guid>? ids, CancellationToken ct)
    {
        var v = new Validator().When(ids is null || ids.Count == 0 || ids.Count > 50, "feeItemIds", Validator.Required);
        v.ThrowIfInvalid();
        var distinct = ids!.Distinct().ToList();
        var fees = await db.FeeItems.AsNoTracking().Where(f => distinct.Contains(f.Id)).ToListAsync(ct);
        if (fees.Count != distinct.Count) new Validator().Add("feeItemIds", Validator.InvalidValue).ThrowIfInvalid();
        var currency = await actor.CurrencyAsync(ct);
        if (fees.Any(f => f.Currency != currency)) throw new BusinessRuleException(FinanceErrors.CurrencyMismatch);
        if (fees.Any(f => !f.IsActive)) throw new BusinessRuleException(FinanceErrors.FeeItemInactive);
        return fees;
    }

    private static bool Applies(FeeItem f, EnrollmentInfo e) =>
        (f.TermId is null || f.TermId == e.TermId) && (f.CourseId is null || f.CourseId == e.CourseId) && (f.BranchId is null || f.BranchId == e.BranchId);

    /// <summary>One draft invoice per enrollment with its not-yet-generated charges. Each enrollment is its own retried unit of work.</summary>
    private async Task<GenerateResult> GenerateForAsync(IReadOnlyList<EnrollmentInfo> enrollments, IReadOnlyList<FeeItem> fees, DateOnly? due, CancellationToken ct)
    {
        var currency = await actor.CurrencyAsync(ct);
        var typeIds = fees.Select(f => f.FeeTypeId).Distinct().ToList();
        var taxable = await db.FeeTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Taxable, ct);
        var settings = await setup.LoadAsync(ct);
        var students = await names.GetAsync(enrollments.Select(e => e.StudentId), ct);
        int created = 0, skipped = 0;
        var invoiceIds = new List<Guid>();
        foreach (var e in enrollments)
        {
            var applicable = fees.Where(f => Applies(f, e)).ToList();
            if (!e.IsActive || applicable.Count == 0 || !students.ContainsKey(e.StudentId)) { skipped++; continue; }
            var invoiceId = await uow.RunAsync(async () =>
            {
                var feeIds = applicable.Select(f => f.Id).ToList();
                var done = await db.GeneratedCharges.AsNoTracking().Where(c => c.EnrollmentId == e.Id && feeIds.Contains(c.FeeItemId))
                    .Select(c => c.FeeItemId).ToListAsync(ct);
                var missing = applicable.Where(f => !done.Contains(f.Id)).ToList();
                if (missing.Count == 0) return (Guid?)null;
                var inv = new Invoice { StudentId = e.StudentId, BranchId = e.BranchId, TermId = e.TermId, Currency = currency, DueDate = due, CreatedBy = actor.UserId };
                var order = 0;
                foreach (var f in missing)
                {
                    inv.Lines.Add(new InvoiceLine { InvoiceId = inv.Id, FeeItemId = f.Id, EnrollmentId = e.Id, Description = f.Name, Quantity = 1,
                        UnitPrice = f.Amount, Taxable = taxable.GetValueOrDefault(f.FeeTypeId), SortOrder = order++ });
                    db.GeneratedCharges.Add(new GeneratedCharge { EnrollmentId = e.Id, FeeItemId = f.Id, InvoiceId = inv.Id });
                }
                if (settings.SiblingDiscountPercent > 0 && await HasSiblingInvoiceAsync(e.StudentId, e.TermId, ct))
                    inv.Discounts.Add(new InvoiceDiscount { InvoiceId = inv.Id, Kind = DiscountKind.Sibling, Type = DiscountType.Percent,
                        Value = settings.SiblingDiscountPercent, Reason = "sibling", RequestedBy = actor.UserId, RequestedAt = actor.UtcNow });
                InvoiceCalculator.Recalculate(inv);
                db.Invoices.Add(inv);
                audit.Write("invoice.generated", "invoice", inv.Id, inv.Id, studentId: inv.StudentId, amount: inv.Total, currency: inv.Currency,
                    details: $"enrollment={e.Id};fees={missing.Count}");
                await db.SaveChangesAsync(ct);
                return (Guid?)inv.Id;
            }, ct);
            if (invoiceId is { } id)
            {
                created++;
                invoiceIds.Add(id);
                await SubmitGeneratedDiscountsAsync(id, students[e.StudentId], ct);
            }
            else skipped++;
        }
        return new GenerateResult(created, skipped, invoiceIds);
    }

    /// <summary>
    /// Automatic sibling discounts of a generated invoice enter the approval workflow after the invoice committed. If the engine refuses
    /// (definition deactivated or unpublished), the discount stays pending without an instance and is decided by the local
    /// maker-checker (same finance.approve + distinct-user rule) — DL-081.
    /// </summary>
    private async Task SubmitGeneratedDiscountsAsync(Guid invoiceId, StudentSummary student, CancellationToken ct)
    {
        var inv = await db.Invoices.AsNoTracking().FirstAsync(i => i.Id == invoiceId, ct);
        var pending = await db.Discounts.Where(d => d.InvoiceId == invoiceId && d.Status == ApprovalStatus.Pending && d.WorkflowInstanceId == null).ToListAsync(ct);
        foreach (var d in pending)
        {
            try
            {
                d.WorkflowInstanceId = await approvals.SubmitAsync(new ApprovalSubject("discount", d.Id, inv.BranchId, inv.StudentId, d.Value, inv.Currency,
                    d.RequestedBy, ApprovalTitle(student)), ct);
            }
            catch (AppException)
            {
                continue;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Workflow inbox title: data only (student name and number), the definition name carries the translated kind.</summary>
    internal static string ApprovalTitle(StudentSummary? s, string? document = null)
    {
        var who = s is null ? "" : $"{s.FirstName} {s.LastName}".Trim() + $" ({s.StudentNumber})";
        var title = document is null ? who : $"{who} · {document}";
        return title.Length > 200 ? title[..200] : title;
    }

    private async Task<bool> HasSiblingInvoiceAsync(Guid studentId, Guid termId, CancellationToken ct)
    {
        var siblings = await people.GetSiblingIdsAsync(studentId, ct);
        if (siblings.Count == 0) return false;
        var ids = siblings.ToList();
        return await db.Invoices.AnyAsync(i => ids.Contains(i.StudentId) && i.TermId == termId && i.Status != InvoiceStatus.Void, ct);
    }

    // Discounts (maker side) -------------------------------------------------------------------------------------------------

    public async Task<DiscountDto> RequestDiscountAsync(Guid invoiceId, DiscountRequest req, CancellationToken ct)
    {
        var inv = await LoadAsync(invoiceId, true, ct);
        await access.EnsureAsync(inv.BranchId, Permissions.FinanceInvoicesManage, ct);
        var v = new Validator().RequiredText("reason", req.Reason, 500);
        var (kind, type) = ValidateDiscount(req.Kind, req.Type, req.Value, inv.Currency, v);
        v.ThrowIfInvalid();
        if (!InvoiceStateMachine.CanEdit(inv.Status)) throw new ConflictException(FinanceErrors.InvalidState, P(("status", Camel(inv.Status))));
        var d = new InvoiceDiscount { InvoiceId = inv.Id, Kind = kind!.Value, Type = type!.Value, Value = req.Value!.Value, Reason = req.Reason!.Trim(),
            RequestedBy = actor.UserId, RequestedAt = actor.UtcNow };
        inv.Discounts.Add(d);
        inv.UpdatedAt = actor.UtcNow; // bump the invoice rowversion: its pending state changed
        audit.Write("discount.requested", "discount", d.Id, inv.Id, studentId: inv.StudentId, amount: d.Value, currency: inv.Currency,
            details: $"{Camel(d.Kind)}:{Camel(d.Type)}");
        await db.SaveChangesAsync(ct);
        await SubmitOrWithdrawAsync(d, inv, ct);
        return InvoiceMapper.ToDto(d);
    }

    /// <summary>
    /// Starts the discount's workflow after the request committed and records the instance; when the engine refuses (e.g. the tenant
    /// deactivated the definition) the request is withdrawn again so nothing is left pending without an approver, and the error surfaces.
    /// </summary>
    private async Task SubmitOrWithdrawAsync(InvoiceDiscount d, Invoice inv, CancellationToken ct)
    {
        var student = (await names.GetAsync([inv.StudentId], ct)).GetValueOrDefault(inv.StudentId);
        try
        {
            d.WorkflowInstanceId = await approvals.SubmitAsync(new ApprovalSubject("discount", d.Id, inv.BranchId, inv.StudentId, d.Value, inv.Currency,
                d.RequestedBy, ApprovalTitle(student, inv.Number)), ct);
            if (d.WorkflowInstanceId is not null) await db.SaveChangesAsync(ct);
        }
        catch (AppException)
        {
            db.Discounts.Remove(d);
            inv.UpdatedAt = actor.UtcNow;
            audit.Write("discount.cancelled", "discount", d.Id, inv.Id, studentId: inv.StudentId, amount: d.Value, currency: inv.Currency,
                details: "workflow_start_failed");
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>The maker edits a discount the approver returned and resubmits it to the same workflow instance (DL-081).</summary>
    public async Task<DiscountDto> ResubmitDiscountAsync(Guid id, DiscountResubmitRequest req, CancellationToken ct)
    {
        var d = await db.Discounts.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        var inv = await LoadAsync(d.InvoiceId, true, ct);
        await access.EnsureAsync(inv.BranchId, Permissions.FinanceInvoicesManage, ct);
        var v = new Validator().RequiredText("reason", req.Reason, 500).RowVersion("rowVersion", req.RowVersion);
        var (kind, type) = ValidateDiscount(req.Kind, req.Type, req.Value, inv.Currency, v);
        v.ThrowIfInvalid();
        if (d.RequestedBy != actor.UserId) throw new ForbiddenException();
        if (d.Status != ApprovalStatus.Returned || d.WorkflowInstanceId is not { } wid)
            throw new ConflictException(FinanceErrors.InvalidState, P(("status", Camel(d.Status))));
        if (!InvoiceStateMachine.CanEdit(inv.Status)) throw new ConflictException(FinanceErrors.InvalidState, P(("status", Camel(inv.Status))));
        CheckRowVersion(d, req.RowVersion);
        d.Kind = kind!.Value;
        d.Type = type!.Value;
        d.Value = req.Value!.Value;
        d.Reason = req.Reason!.Trim();
        d.Status = ApprovalStatus.Pending;
        d.RequestedAt = actor.UtcNow;
        inv.UpdatedAt = actor.UtcNow;
        audit.Write("discount.resubmitted", "discount", d.Id, inv.Id, studentId: inv.StudentId, amount: d.Value, currency: inv.Currency,
            details: $"{Camel(d.Kind)}:{Camel(d.Type)};workflow={wid}");
        await db.SaveChangesAsync(ct);
        await approvals.ResubmitAsync(wid, ct);
        return InvoiceMapper.ToDto(await db.Discounts.AsNoTracking().FirstAsync(x => x.Id == id, ct));
    }

    private static (DiscountKind?, DiscountType?) ValidateDiscount(string? kindText, string? typeText, decimal? value, string currency, Validator v)
    {
        var kind = ParseEnum<DiscountKind>(kindText, "kind", v, required: true);
        var type = ParseEnum<DiscountType>(typeText, "type", v, required: true);
        if (type == DiscountType.Percent)
            v.When(value is null || value <= 0 || value > 100 || decimal.Round(value.Value, 2) != value, "value", Validator.InvalidValue);
        else if (type == DiscountType.Fixed) Amount(value, "value", currency, v);
        return (kind, type);
    }

    // Installments and late fees ---------------------------------------------------------------------------------------------

    public Task<InvoiceDto> SetInstallmentsAsync(Guid id, InstallmentsRequest req, CancellationToken ct) => uow.RunAsync(async () =>
    {
        var inv = await LoadForWriteAsync(id, req.RowVersion, ct);
        if (inv.Status is not (InvoiceStatus.Draft or InvoiceStatus.Issued)) throw new ConflictException(FinanceErrors.InvalidState, P(("status", Camel(inv.Status))));
        if (inv.PaidAmount > 0 || inv.Installments.Any(i => i.PaidAmount > 0 || i.LateFeeCharged)) throw new BusinessRuleException(FinanceErrors.InstallmentsLocked);
        var v = new Validator();
        List<(DateOnly DueDate, decimal Amount)> plan = [];
        if (req.Items is { Count: > 0 } items)
        {
            v.When(items.Count > 24, "items", Validator.InvalidValue);
            for (var i = 0; i < items.Count; i++)
            {
                var d = Validator.ParseDate(items[i].DueDate, $"items[{i}].dueDate", v);
                if (string.IsNullOrWhiteSpace(items[i].DueDate)) v.Add($"items[{i}].dueDate", Validator.Required);
                var a = Amount(items[i].Amount, $"items[{i}].amount", inv.Currency, v);
                if (d is not null && a is not null) plan.Add((d.Value, a.Value));
            }
            v.ThrowIfInvalid();
            plan = plan.OrderBy(p => p.DueDate).ToList();
        }
        else
        {
            v.When(req.Count is null or < 1 or > 24, "count", Validator.InvalidValue);
            v.When(req.IntervalDays is null or < 1 or > 366, "intervalDays", Validator.InvalidValue);
            if (string.IsNullOrWhiteSpace(req.FirstDueDate)) v.Add("firstDueDate", Validator.Required);
            var first = Validator.ParseDate(req.FirstDueDate, "firstDueDate", v);
            v.ThrowIfInvalid();
            if (inv.Status == InvoiceStatus.Draft) InvoiceCalculator.Recalculate(inv);
            if (inv.Total <= 0) throw new BusinessRuleException(FinanceErrors.InvoiceEmpty);
            plan = InstallmentPlanner.EqualSplit(inv.Total, req.Count!.Value, first!.Value, req.IntervalDays!.Value, inv.Currency).ToList();
        }
        if (inv.Status == InvoiceStatus.Draft) InvoiceCalculator.Recalculate(inv);
        if (plan.Sum(p => p.Amount) != inv.Total) throw new BusinessRuleException(FinanceErrors.InstallmentsMismatch, P(("total", inv.Total)));
        db.Installments.RemoveRange(inv.Installments);
        inv.Installments.Clear();
        var seq = 1;
        foreach (var (due, amount) in plan) inv.Installments.Add(new Installment { InvoiceId = inv.Id, SeqNo = seq++, DueDate = due, Amount = amount });
        if (inv.Status == InvoiceStatus.Issued) inv.DueDate = plan[^1].DueDate;
        inv.UpdatedAt = actor.UtcNow;
        audit.Write("installments.set", "invoice", inv.Id, inv.Id, studentId: inv.StudentId, amount: inv.Total, currency: inv.Currency, details: $"count={plan.Count}");
        await db.SaveChangesAsync(ct);
        return await mapper.ToDtoAsync(inv, await actor.TodayAsync(ct), ct);
    }, ct);

    public async Task<LateFeeResult> ApplyLateFeesAsync(LateFeeRequest req, CancellationToken ct)
    {
        var v = new Validator();
        var asOf = OptionalDate(req.AsOf, "asOf", v) ?? await actor.TodayAsync(ct);
        v.ThrowIfInvalid();
        var scope = await access.ScopeAsync(Permissions.FinanceInvoicesManage, ct);
        if (scope.IsEmpty) throw new ForbiddenException();
        var settings = await setup.LoadAsync(ct);
        if (!settings.LateFeeEnabled || settings.LateFeeValue <= 0) return new LateFeeResult(0);
        var cutoff = asOf.AddDays(-settings.LateFeeGraceDays);
        var candidates = await db.Invoices.AsNoTracking().Include(i => i.Installments)
            .Where(i => i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid).Select(i => new { i.Id, i.BranchId }).ToListAsync(ct);
        var applied = 0;
        foreach (var c in candidates.Where(c => scope.Includes(c.BranchId)))
        {
            applied += await uow.RunAsync(async () =>
            {
                var inv = await LoadAsync(c.Id, true, ct);
                if (!inv.IsOpen || inv.Balance <= 0) return 0;
                var count = 0;
                decimal Fee(decimal basis) => settings.LateFeeType == LateFeeType.Fixed ? settings.LateFeeValue : Money.Percent(basis, settings.LateFeeValue, inv.Currency);
                if (inv.Installments.Count > 0)
                {
                    foreach (var inst in inv.Installments.Where(i => i.Remaining > 0 && !i.LateFeeCharged && i.DueDate < cutoff).OrderBy(i => i.DueDate))
                    {
                        var fee = Fee(inst.Remaining);
                        inst.LateFeeCharged = true;
                        if (fee <= 0) continue;
                        AddLateFee(inv, fee, inst.Id);
                        count++;
                    }
                }
                else if (inv.DueDate is { } due && due < cutoff && !inv.Adjustments.Any(a => a.Kind == AdjustmentKind.LateFee))
                {
                    var fee = Fee(inv.Balance);
                    if (fee > 0) { AddLateFee(inv, fee, null); count++; }
                }
                if (count > 0)
                {
                    InvoiceCalculator.UpdatePaymentStatus(inv);
                    await db.SaveChangesAsync(ct);
                }
                return count;
            }, ct);
        }
        return new LateFeeResult(applied);
    }

    private void AddLateFee(Invoice inv, decimal fee, Guid? installmentId)
    {
        var adj = new InvoiceAdjustment { InvoiceId = inv.Id, Kind = AdjustmentKind.LateFee, Amount = fee, Reason = "lateFee", InstallmentId = installmentId,
            CreatedAt = actor.UtcNow, CreatedBy = actor.UserId };
        inv.Adjustments.Add(adj);
        inv.LateFeeTotal += fee;
        audit.Write("invoice.lateFee", "lateFee", adj.Id, inv.Id, studentId: inv.StudentId, amount: fee, currency: inv.Currency);
    }
}
