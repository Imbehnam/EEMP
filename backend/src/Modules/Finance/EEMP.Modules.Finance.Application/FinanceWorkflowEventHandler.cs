using EEMP.Modules.Finance.Domain;
using EEMP.Modules.Workflow.Contracts;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Finance.Application.Fmt;

namespace EEMP.Modules.Finance.Application;

/// <summary>
/// Applies workflow outcomes to discount and refund requests (workflow contract §7, DL-081). Runs after the workflow transition
/// committed, in a fresh scope inside the event's tenant with no user. Idempotent: every change is guarded by the request's current
/// status and its recorded workflow instance, so a redelivered event is a no-op; events for instances Finance did not start (or for
/// requests withdrawn in Finance) are ignored.
/// <list type="bullet">
/// <item>completed/approved → apply (discount amount recalculated on its draft invoice; refund taken from the payment's credit)</item>
/// <item>completed/rejected or cancelled → rejected</item>
/// <item>returned → returned (the maker may edit and resubmit, or withdraw it); resubmitted → pending again</item>
/// </list>
/// If the subject can no longer be applied when approval arrives (the invoice left draft, the credit was used meanwhile), the request
/// is rejected with a recorded reason instead of failing the event forever.
/// </summary>
public sealed class FinanceWorkflowEventHandler(IFinanceDbContext db, FinanceAuditor audit, FinanceActor actor) : IWorkflowEventHandler
{
    public const string InvoiceNotEditable = "finance.invoice_not_editable";

    public string SubjectModule => FinanceApprovalDefinitions.SubjectModule;

    public Task HandleAsync(WorkflowEvent workflowEvent, CancellationToken ct) => workflowEvent.SubjectType switch
    {
        "discount" => DiscountAsync(workflowEvent, ct),
        "refund" => RefundAsync(workflowEvent, ct),
        _ => Task.CompletedTask,
    };

    private async Task DiscountAsync(WorkflowEvent e, CancellationToken ct)
    {
        var d = await db.Discounts.FirstOrDefaultAsync(x => x.Id == e.SubjectId, ct);
        if (d is null || d.WorkflowInstanceId != e.InstanceId) return;
        var inv = await InvoiceMapper.WithDetails(db.Invoices).FirstAsync(i => i.Id == d.InvoiceId, ct);
        var next = Next(d.Status, e);
        if (next is null) return;
        var note = Note(e.Comment);
        if (next == ApprovalStatus.Approved && inv.Status != InvoiceStatus.Draft)
        {
            next = ApprovalStatus.Rejected;
            note = InvoiceNotEditable;
        }
        d.Status = next.Value;
        if (next is ApprovalStatus.Approved or ApprovalStatus.Rejected)
        {
            d.DecidedBy = e.ActorUserId;
            d.DecidedAt = e.OccurredAt;
            d.DecisionNote = note;
        }
        InvoiceCalculator.Recalculate(inv);
        inv.UpdatedAt = actor.UtcNow;
        audit.Write($"discount.{Action(next.Value)}", "discount", d.Id, inv.Id, studentId: inv.StudentId, amount: next == ApprovalStatus.Approved ? d.Amount : d.Value,
            currency: inv.Currency, details: Details(e, d.RequestedBy, note), actorUserId: e.ActorUserId);
        await db.SaveChangesAsync(ct);
    }

    private async Task RefundAsync(WorkflowEvent e, CancellationToken ct)
    {
        var r = await db.Refunds.FirstOrDefaultAsync(x => x.Id == e.SubjectId, ct);
        if (r is null || r.WorkflowInstanceId != e.InstanceId) return;
        var next = Next(r.Status, e);
        if (next is null) return;
        var p = await db.Payments.FirstAsync(x => x.Id == r.PaymentId, ct);
        var note = Note(e.Comment);
        if (next == ApprovalStatus.Approved)
        {
            if (r.Amount > p.Unallocated)
            {
                next = ApprovalStatus.Rejected;
                note = FinanceErrors.RefundExceedsCredit;
            }
            else p.Refunded += r.Amount;
        }
        r.Status = next.Value;
        if (next is ApprovalStatus.Approved or ApprovalStatus.Rejected)
        {
            r.DecidedBy = e.ActorUserId;
            r.DecidedAt = e.OccurredAt;
            r.DecisionNote = note;
        }
        p.UpdatedAt = actor.UtcNow;
        audit.Write($"refund.{Action(next.Value)}", "refund", r.Id, null, p.Id, r.StudentId, r.Amount, r.Currency, Details(e, r.RequestedBy, note),
            actorUserId: e.ActorUserId);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The status an event moves an open request to, or null when it does not apply (already decided, replay, unknown event).</summary>
    public static ApprovalStatus? Next(ApprovalStatus current, WorkflowEvent e)
    {
        if (current is not (ApprovalStatus.Pending or ApprovalStatus.Returned)) return null;
        return (e.EventType, e.Outcome) switch
        {
            (WorkflowEventTypes.Completed, WorkflowOutcomes.Approved) => ApprovalStatus.Approved,
            (WorkflowEventTypes.Completed, WorkflowOutcomes.Rejected or WorkflowOutcomes.Cancelled) => ApprovalStatus.Rejected,
            (WorkflowEventTypes.Returned, _) when current == ApprovalStatus.Pending => ApprovalStatus.Returned,
            (WorkflowEventTypes.Resubmitted, _) when current == ApprovalStatus.Returned => ApprovalStatus.Pending,
            _ => null,
        };
    }

    private static string Action(ApprovalStatus s) => s switch
    {
        ApprovalStatus.Approved => "approved",
        ApprovalStatus.Rejected => "rejected",
        ApprovalStatus.Returned => "returned",
        _ => "resubmitted",
    };

    private static string? Note(string? comment) => Clean(comment) is { } c ? (c.Length > 500 ? c[..500] : c) : null;

    private static string Details(WorkflowEvent e, Guid? requestedBy, string? reason) =>
        $"workflow={e.InstanceId};outcome={e.Outcome};requestedBy={requestedBy}" + (reason is FinanceErrors.RefundExceedsCredit or InvoiceNotEditable ? $";reason={reason}" : "");
}
