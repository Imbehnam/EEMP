using EEMP.Modules.Consultation.Domain;
using EEMP.Modules.Identity.Contracts;
using EEMP.Modules.People.Contracts;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Paging;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Consultation.Application.Fmt;

namespace EEMP.Modules.Consultation.Application;

/// <summary>
/// Support tickets (DL-094). Portal requesters see only tickets they created and never internal messages; staff see tickets of their
/// branches with support.tickets.read and act with support.tickets.manage.
/// </summary>
public sealed class SupportService(IConsultationDbContext db, ConsultationActor actor, IConsultationClock clock, IPeopleDirectory people,
    IUserDirectory users)
{
    public const int MaxSubject = 200;
    public const int MaxBody = 4000;
    private const int MaxNumberAttempts = 3;

    // ---- Portal ----

    public async Task<IReadOnlyList<TicketSummaryDto>> PortalListAsync(CancellationToken ct)
    {
        var me = actor.UserId;
        var rows = await db.Tickets.AsNoTracking().Where(t => t.CreatedByUserId == me).OrderByDescending(t => t.LastMessageAt).Take(200).ToListAsync(ct);
        return await SummariesAsync(rows, includeAssignee: false, ct);
    }

    public async Task<TicketDetailDto> PortalCreateAsync(CreateTicketRequest req, CancellationToken ct)
    {
        var subject = Clean(req.Subject);
        var body = Clean(req.Body);
        var v = new Validator().RequiredText("subject", subject, MaxSubject).RequiredText("body", body, MaxBody);
        if (!TryEnum<TicketCategory>(req.Category, out var category)) v.Add("category", Validator.InvalidValue);
        v.ThrowIfInvalid();
        var student = await actor.PortalStudentAsync(req.StudentId, ct);
        var me = actor.UserId;
        var now = clock.UtcNow;
        for (var attempt = 1; ; attempt++)
        {
            var number = (await db.Tickets.MaxAsync(t => (int?)t.Number, ct) ?? 0) + 1;
            var ticket = new Ticket
            {
                Number = number, BranchId = student.BranchId, StudentId = student.Id, CreatedByUserId = me, Subject = subject!, Category = category,
                LastMessageAt = now,
            };
            db.Tickets.Add(ticket);
            db.TicketMessages.Add(new TicketMessage { TicketId = ticket.Id, AuthorUserId = me, FromStaff = false, Body = body!, CreatedAt = now });
            try
            {
                await db.SaveChangesAsync(ct);
                return await DetailAsync(ticket, staffView: false, canManage: false, ct);
            }
            catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException && attempt < MaxNumberAttempts)
            {
                db.ResetTracking(); // number taken concurrently; take the next one
            }
        }
    }

    public async Task<TicketDetailDto> PortalGetAsync(Guid id, CancellationToken ct) =>
        await DetailAsync(await PortalTicketAsync(id, track: false, ct), staffView: false, canManage: false, ct);

    public async Task<TicketDetailDto> PortalMessageAsync(Guid id, TicketMessageRequest req, CancellationToken ct)
    {
        var body = Clean(req.Body);
        new Validator().RequiredText("body", body, MaxBody).ThrowIfInvalid();
        var t = await PortalTicketAsync(id, track: true, ct);
        if (t.Status == TicketStatus.Closed) throw new ConflictException(ConsultationErrors.TicketClosed);
        AddMessage(t, body!, fromStaff: false, isInternal: false);
        await db.SaveChangesAsync(ct);
        return await DetailAsync(t, staffView: false, canManage: false, ct);
    }

    public async Task<TicketDetailDto> PortalCloseAsync(Guid id, TicketRowVersionRequest req, CancellationToken ct)
    {
        new Validator().RowVersion("rowVersion", req.RowVersion).ThrowIfInvalid();
        var t = await PortalTicketAsync(id, track: true, ct);
        if (t.Status != TicketStatus.Closed)
        {
            db.SetOriginalRowVersion(t, Convert.FromBase64String(req.RowVersion!));
            t.Status = TicketStatus.Closed;
            t.ClosedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return await DetailAsync(t, staffView: false, canManage: false, ct);
    }

    private async Task<Ticket> PortalTicketAsync(Guid id, bool track, CancellationToken ct)
    {
        var me = actor.UserId;
        var q = track ? db.Tickets : db.Tickets.AsNoTracking();
        return await q.FirstOrDefaultAsync(t => t.Id == id && t.CreatedByUserId == me, ct) ?? throw new NotFoundException();
    }

    // ---- Staff ----

    public async Task<PagedResult<TicketSummaryDto>> ListAsync(PageRequest page, TicketListQuery query, CancellationToken ct)
    {
        var v = new Validator();
        TicketStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (TryEnum<TicketStatus>(query.Status, out var s)) status = s;
            else v.Add("status", Validator.InvalidValue);
        }
        var assignee = string.IsNullOrWhiteSpace(query.Assignee) ? "any" : query.Assignee;
        if (assignee is not ("any" or "me" or "unassigned")) v.Add("assignee", Validator.InvalidValue);
        v.ThrowIfInvalid();
        var scope = await actor.ScopeAsync(Permissions.SupportTicketsRead, ct);
        if (scope.IsEmpty) throw new ForbiddenException();
        var q = db.Tickets.AsNoTracking().Where(ScopeFilter.Covers<Ticket>(scope, t => t.BranchId));
        if (status is { } st) q = q.Where(t => t.Status == st);
        if (query.BranchId is { } bid) q = q.Where(t => t.BranchId == bid);
        var me = actor.UserId;
        if (assignee == "me") q = q.Where(t => t.AssigneeUserId == me);
        else if (assignee == "unassigned") q = q.Where(t => t.AssigneeUserId == null);
        if (page.Search is { } search)
        {
            var n = int.TryParse(search.TrimStart('#'), out var num) ? num : -1;
            q = q.Where(t => t.Subject.Contains(search) || t.Number == n);
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(t => t.LastMessageAt).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<TicketSummaryDto>(await SummariesAsync(rows, includeAssignee: true, ct), page.Page, page.PageSize, total);
    }

    public async Task<TicketDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var (t, manage) = await StaffTicketAsync(id, track: false, ct);
        return await DetailAsync(t, staffView: true, manage, ct);
    }

    public async Task<TicketDetailDto> MessageAsync(Guid id, TicketMessageRequest req, CancellationToken ct)
    {
        var body = Clean(req.Body);
        new Validator().RequiredText("body", body, MaxBody).ThrowIfInvalid();
        var (t, manage) = await StaffTicketAsync(id, track: true, ct);
        if (!manage) throw new ForbiddenException();
        if (t.Status == TicketStatus.Closed) throw new ConflictException(ConsultationErrors.TicketClosed);
        AddMessage(t, body!, fromStaff: true, req.IsInternal);
        await db.SaveChangesAsync(ct);
        return await DetailAsync(t, staffView: true, manage, ct);
    }

    public async Task<TicketDetailDto> UpdateAsync(Guid id, UpdateTicketRequest req, CancellationToken ct)
    {
        var v = new Validator().RowVersion("rowVersion", req.RowVersion);
        if (!TryEnum<TicketStatus>(req.Status, out var status)) v.Add("status", Validator.InvalidValue);
        if (!TryEnum<TicketPriority>(req.Priority, out var priority)) v.Add("priority", Validator.InvalidValue);
        v.ThrowIfInvalid();
        var (t, manage) = await StaffTicketAsync(id, track: true, ct);
        if (!manage) throw new ForbiddenException();
        if (t.Status == TicketStatus.Closed && status != TicketStatus.Closed) throw new ConflictException(ConsultationErrors.TicketClosed);
        if (req.AssigneeUserId is { } a && a != t.AssigneeUserId && !await users.ExistsAsync(a, ct))
            throw new ValidationException(new Dictionary<string, string[]> { ["assigneeUserId"] = [ConsultationErrors.UnknownAssignee] });
        db.SetOriginalRowVersion(t, Convert.FromBase64String(req.RowVersion!));
        if (status == TicketStatus.Closed && t.Status != TicketStatus.Closed) t.ClosedAt = clock.UtcNow;
        t.Status = status;
        t.Priority = priority;
        t.AssigneeUserId = req.AssigneeUserId;
        await db.SaveChangesAsync(ct);
        return await DetailAsync(t, staffView: true, manage, ct);
    }

    private async Task<(Ticket Ticket, bool Manage)> StaffTicketAsync(Guid id, bool track, CancellationToken ct)
    {
        var q = track ? db.Tickets : db.Tickets.AsNoTracking();
        var t = await q.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        var read = (await actor.ScopeAsync(Permissions.SupportTicketsRead, ct)).Includes(t.BranchId);
        var manage = (await actor.ScopeAsync(Permissions.SupportTicketsManage, ct)).Includes(t.BranchId);
        if (!read && !manage) throw new NotFoundException();
        return (t, manage);
    }

    // ---- Shared ----

    private void AddMessage(Ticket t, string body, bool fromStaff, bool isInternal)
    {
        var now = clock.UtcNow;
        db.TicketMessages.Add(new TicketMessage { TicketId = t.Id, AuthorUserId = actor.UserId, FromStaff = fromStaff, Body = body, IsInternal = isInternal, CreatedAt = now });
        t.Status = TicketRules.AfterMessage(t.Status, fromStaff, isInternal);
        if (!isInternal) t.LastMessageAt = now;
        t.UpdatedAt = now; // the ticket row changes with every message, so its rowversion moves too
    }

    private async Task<List<TicketSummaryDto>> SummariesAsync(List<Ticket> rows, bool includeAssignee, CancellationToken ct)
    {
        var students = await people.GetStudentsAsync(rows.Select(r => r.StudentId).Distinct().ToList(), ct);
        var assignees = includeAssignee
            ? await users.GetDisplayNamesAsync(rows.Where(r => r.AssigneeUserId != null).Select(r => r.AssigneeUserId!.Value).Distinct().ToList(), ct)
            : new Dictionary<Guid, string>();
        return rows.Select(t => new TicketSummaryDto(t.Id, t.Number, t.BranchId, t.StudentId, StudentName(students.GetValueOrDefault(t.StudentId)), t.Subject,
            Camel(t.Category), Camel(t.Priority), Camel(t.Status), includeAssignee ? t.AssigneeUserId : null,
            t.AssigneeUserId is { } a ? assignees.GetValueOrDefault(a) : null, t.CreatedAt, t.LastMessageAt, Rv(t.RowVersion))).ToList();
    }

    private async Task<TicketDetailDto> DetailAsync(Ticket t, bool staffView, bool canManage, CancellationToken ct)
    {
        var q = db.TicketMessages.AsNoTracking().Where(m => m.TicketId == t.Id);
        if (!staffView) q = q.Where(m => !m.IsInternal);
        var messages = await q.OrderBy(m => m.CreatedAt).ToListAsync(ct);
        var names = await users.GetDisplayNamesAsync(messages.Select(m => m.AuthorUserId).Distinct().ToList(), ct);
        var me = actor.UserId;
        var summary = (await SummariesAsync([t], staffView, ct))[0];
        return new TicketDetailDto(summary, messages.Select(m => new TicketMessageDto(m.Id,
            // The portal sees staff replies as coming from the team; staff names are not disclosed to families.
            !staffView && m.FromStaff ? string.Empty : names.GetValueOrDefault(m.AuthorUserId, ""),
            m.FromStaff, m.IsInternal, m.Body, m.CreatedAt, m.AuthorUserId == me)).ToList(), canManage);
    }
}
