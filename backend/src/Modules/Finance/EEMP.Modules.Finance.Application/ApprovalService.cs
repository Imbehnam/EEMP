using EEMP.Modules.Finance.Domain;
using EEMP.Modules.Workflow.Contracts;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Finance.Application.Fmt;

namespace EEMP.Modules.Finance.Application;

/// <summary>
/// The approval seam (DL-063, DL-081). Discount and refund requests are decided by the EEMP-011 workflow engine: the maker's request
/// starts an instance of the tenant's "discount-approval" / "refund-approval" definition, approvers act from the workflow inbox (or the
/// thin /finance wrappers), and <see cref="FinanceWorkflowEventHandler"/> applies the outcome. Requests created before the
/// integration (no workflow instance) are still decided by the local M3 maker-checker.
/// </summary>
public interface IFinanceApprovals
{
    /// <summary>
    /// Called after the pending request was saved. Starts the workflow instance (idempotency key <c>finance:{kind}:{id}</c>) and returns
    /// its id, or null when this implementation decides locally.
    /// </summary>
    Task<Guid?> SubmitAsync(ApprovalSubject subject, CancellationToken ct);

    /// <summary>Local maker-checker (requests without a workflow instance): throws unless the current user may decide.</summary>
    Task EnsureCanDecideLocallyAsync(Guid branchId, Guid? requestedBy, CancellationToken ct);

    /// <summary>Approve/reject through the workflow as the current user (engine checks step permission and maker-checker).</summary>
    Task DecideAsync(Guid workflowInstanceId, bool approve, string? note, CancellationToken ct);

    /// <summary>The maker edited a returned request: resubmit the workflow instance.</summary>
    Task ResubmitAsync(Guid workflowInstanceId, CancellationToken ct);

    /// <summary>The request was withdrawn in Finance: cancel the workflow instance (no-op when it already finished).</summary>
    Task CancelAsync(Guid workflowInstanceId, string? reason, CancellationToken ct);
}

/// <summary>What is being approved: kind "discount" or "refund", the record id, its branch/amount for routing and the inbox title.</summary>
public sealed record ApprovalSubject(string Kind, Guid Id, Guid BranchId, Guid StudentId, decimal Amount, string Currency, Guid? RequestedBy, string Title);

/// <summary>Finance's default approval definitions (DL-083): one step that requires finance.approve in the request's branch.</summary>
public static class FinanceApprovalDefinitions
{
    public const string SubjectModule = "finance";
    public const string DiscountKey = "discount-approval";
    public const string RefundKey = "refund-approval";
    public const string StepName = "Finance approval";
    public const int SlaHours = 48;

    public static WorkflowDefinitionTemplate For(string kind) => kind switch
    {
        "discount" => new(DiscountKey, "Discount approval", SubjectModule, "discount", [new(StepName, Permissions.FinanceApprove, SlaHours)]),
        "refund" => new(RefundKey, "Refund approval", SubjectModule, "refund", [new(StepName, Permissions.FinanceApprove, SlaHours)]),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

/// <summary>DL-083: finance approval steps can only require finance.approve, so configuring a definition cannot hand money decisions to non-finance staff.</summary>
public sealed class FinanceWorkflowPolicy : IWorkflowSubjectPolicy
{
    private static readonly string[] Allowed = [Permissions.FinanceApprove];
    public string SubjectModule => FinanceApprovalDefinitions.SubjectModule;
    public IReadOnlyCollection<string> AllowedStepPermissions(string subjectType) => Allowed;
}

public sealed class WorkflowFinanceApprovals(IWorkflowService workflow, FinanceAccess access, FinanceActor actor) : IFinanceApprovals
{
    public async Task<Guid?> SubmitAsync(ApprovalSubject subject, CancellationToken ct)
    {
        var template = FinanceApprovalDefinitions.For(subject.Kind);
        await workflow.EnsureDefinitionAsync(template, ct);
        var info = await workflow.StartAsync(new StartWorkflowCommand(template.Key, template.SubjectModule, template.SubjectType, subject.Id,
            subject.BranchId, subject.Title, $"finance:{subject.Kind}:{subject.Id}", subject.RequestedBy), ct);
        return info.Id;
    }

    public async Task EnsureCanDecideLocallyAsync(Guid branchId, Guid? requestedBy, CancellationToken ct)
    {
        await access.EnsureAsync(branchId, Permissions.FinanceApprove, ct);
        if (requestedBy is not null && requestedBy == actor.UserId) throw new ForbiddenException(FinanceErrors.MakerChecker);
    }

    public async Task DecideAsync(Guid workflowInstanceId, bool approve, string? note, CancellationToken ct)
    {
        try
        {
            await workflow.ActAsync(workflowInstanceId, FinanceApprovalDefinitions.SubjectModule, approve ? "approve" : "reject", note, ct);
        }
        catch (ForbiddenException ex) when (ex.Code == SelfApprovalForbidden)
        {
            // The /finance wrappers keep their M3 error contract: the maker cannot decide their own request.
            throw new ForbiddenException(FinanceErrors.MakerChecker);
        }
    }

    private const string SelfApprovalForbidden = "workflow.self_approval_forbidden";

    public Task ResubmitAsync(Guid workflowInstanceId, CancellationToken ct) =>
        workflow.ActAsync(workflowInstanceId, FinanceApprovalDefinitions.SubjectModule, "resubmit", null, ct);

    public Task CancelAsync(Guid workflowInstanceId, string? reason, CancellationToken ct) =>
        workflow.CancelAsync(workflowInstanceId, FinanceApprovalDefinitions.SubjectModule, reason, ct);
}

public sealed class ApprovalService(IFinanceDbContext db, FinanceActor actor, FinanceAccess access, FinanceAuditor audit, FinanceUnitOfWork uow,
    IFinanceApprovals approvals, StudentNames names)
{
    public static RefundDto ToDto(Refund r) => new(r.Id, r.PaymentId, r.StudentId, r.BranchId, r.Amount, r.Currency, r.Reason, Camel(r.Status),
        r.RequestedBy, r.RequestedAt, r.DecidedBy, r.DecidedAt, r.DecisionNote, Rv(r.RowVersion), r.WorkflowInstanceId);

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
                x.BranchId, x.d.Value, x.Currency, $"{Camel(x.d.Kind)}:{Camel(x.d.Type)}", x.d.Reason, x.d.RequestedBy, x.d.RequestedAt, Rv(x.d.RowVersion), x.d.WorkflowInstanceId))
            .Concat(refunds.Where(r => Visible(r.BranchId)).Select(r => new ApprovalItemDto("refund", r.Id, null, r.PaymentId, r.StudentId, Name(r.StudentId), r.BranchId,
                r.Amount, r.Currency, "refund", r.Reason, r.RequestedBy, r.RequestedAt, Rv(r.RowVersion), r.WorkflowInstanceId)))
            .OrderBy(i => i.RequestedAt).ToList();
        return items;
    }

    /// <summary>
    /// Approve/reject a discount. Workflow-backed requests (DL-082) are decided through the engine as the current user — the same rules as
    /// the workflow inbox — and the outcome is applied by <see cref="FinanceWorkflowEventHandler"/>; legacy requests use the local checker.
    /// </summary>
    public async Task<DiscountDto> DecideDiscountAsync(Guid id, bool approve, DecisionRequest req, CancellationToken ct)
    {
        var note = Clean(req.Note);
        var v = new Validator().OptionalText("note", note, 500);
        var d = await db.Discounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        if (d.WorkflowInstanceId is not { } wid)
        {
            v.ThrowIfInvalid();
            return await DecideDiscountLocallyAsync(id, approve, req, ct);
        }
        v.When(!approve && note is null, "note", Validator.Required).ThrowIfInvalid();
        var branchId = await db.Invoices.AsNoTracking().Where(i => i.Id == d.InvoiceId).Select(i => i.BranchId).FirstAsync(ct);
        await access.EnsureAsync(branchId, Permissions.FinanceApprove, ct);
        if (d.Status == (approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected)) return InvoiceMapper.ToDto(d); // replayed decision
        if (d.Status != ApprovalStatus.Pending) throw new ConflictException(FinanceErrors.InvalidState, P(("status", Camel(d.Status))));
        await approvals.DecideAsync(wid, approve, note, ct);
        return InvoiceMapper.ToDto(await db.Discounts.AsNoTracking().FirstAsync(x => x.Id == id, ct));
    }

    private Task<DiscountDto> DecideDiscountLocallyAsync(Guid id, bool approve, DecisionRequest req, CancellationToken ct) => uow.RunAsync(async () =>
    {
        var d = await db.Discounts.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        var inv = await InvoiceMapper.WithDetails(db.Invoices).FirstAsync(i => i.Id == d.InvoiceId, ct);
        await approvals.EnsureCanDecideLocallyAsync(inv.BranchId, d.RequestedBy, ct);
        if (d.Status == (approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected)) return InvoiceMapper.ToDto(d); // replayed decision
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

    /// <summary>Withdraws an open (pending or returned) discount; its workflow instance, if any, is cancelled afterwards.</summary>
    public async Task CancelDiscountAsync(Guid id, CancellationToken ct)
    {
        var d = await db.Discounts.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        var inv = await db.Invoices.FirstAsync(i => i.Id == d.InvoiceId, ct);
        if (d.RequestedBy == actor.UserId) await access.EnsureAsync(inv.BranchId, Permissions.FinanceInvoicesManage, ct);
        else await access.EnsureAsync(inv.BranchId, Permissions.FinanceApprove, ct);
        if (!d.IsOpen) throw new ConflictException(FinanceErrors.InvalidState, P(("status", Camel(d.Status))));
        var wid = d.WorkflowInstanceId;
        db.Discounts.Remove(d);
        inv.UpdatedAt = actor.UtcNow;
        audit.Write("discount.cancelled", "discount", d.Id, inv.Id, studentId: inv.StudentId, amount: d.Value, currency: inv.Currency,
            details: wid is { } w ? $"workflow={w}" : null);
        await db.SaveChangesAsync(ct);
        // After the commit: the handler then finds no discount and ignores the cancellation event.
        if (wid is { } instanceId) await approvals.CancelAsync(instanceId, "finance:discount_cancelled", ct);
    }

    /// <summary>Approve/reject a refund; workflow-backed requests go through the engine (DL-082), legacy ones through the local checker.</summary>
    public async Task<RefundDto> DecideRefundAsync(Guid id, bool approve, DecisionRequest req, CancellationToken ct)
    {
        var note = Clean(req.Note);
        var v = new Validator().OptionalText("note", note, 500);
        var r = await db.Refunds.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        if (r.WorkflowInstanceId is not { } wid)
        {
            v.ThrowIfInvalid();
            return await DecideRefundLocallyAsync(id, approve, req, ct);
        }
        v.When(!approve && note is null, "note", Validator.Required).ThrowIfInvalid();
        await access.EnsureAsync(r.BranchId, Permissions.FinanceApprove, ct);
        if (r.Status == (approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected)) return ToDto(r); // replayed decision
        if (r.Status != ApprovalStatus.Pending) throw new ConflictException(FinanceErrors.InvalidState, P(("status", Camel(r.Status))));
        await approvals.DecideAsync(wid, approve, note, ct);
        return ToDto(await db.Refunds.AsNoTracking().FirstAsync(x => x.Id == id, ct));
    }

    private Task<RefundDto> DecideRefundLocallyAsync(Guid id, bool approve, DecisionRequest req, CancellationToken ct) => uow.RunAsync(async () =>
    {
        var r = await db.Refunds.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        await approvals.EnsureCanDecideLocallyAsync(r.BranchId, r.RequestedBy, ct);
        if (r.Status == (approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected)) return ToDto(r); // replayed decision
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
