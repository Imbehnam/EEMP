using EEMP.Infrastructure.Audit;
using EEMP.Infrastructure.Authorization;
using EEMP.Infrastructure.Persistence;
using EEMP.Infrastructure.Web;
using EEMP.Modules.Consultation.Application;
using EEMP.Modules.Consultation.Contracts;
using EEMP.Modules.Consultation.Domain;
using EEMP.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EEMP.Modules.Consultation.Infrastructure;

public static class ConsultationModule
{
    public static IServiceCollection AddConsultationModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<ConsultationDbContext>(ConsultationDbContext.SchemaName);
        services.AddScoped<IConsultationDbContext>(sp => sp.GetRequiredService<ConsultationDbContext>());
        services.TryAddSingleton<IConsultationClock, SystemConsultationClock>();
        services.AddScoped<INotesAccessLog, AuditNotesAccessLog>();
        services.AddScoped<IConsultationBranchUsage, ConsultationBranchUsage>();
        services.AddScoped<ConsultationActor>();
        services.AddScoped<SettingsService>();
        services.AddScoped<ConsultantService>();
        services.AddScoped<SlotService>();
        services.AddScoped<BookingService>();
        services.AddScoped<PortalConsultationService>();
        services.AddScoped<SupportService>();
        return services;
    }

    /// <summary>
    /// Every endpoint requires a tenant user. Consultation endpoints combine consultant ownership with branch-scoped permissions, and
    /// portal endpoints authorize by the student/guardian relationship, so the services enforce access (invisible records are 404).
    /// </summary>
    public static IEndpointRouteBuilder MapConsultationEndpoints(this IEndpointRouteBuilder api)
    {
        var c = api.MapGroup("/consultation").WithTags("Consultation").RequireAuthorization(Policies.TenantUser);
        c.MapGet("/settings", async (SettingsService s, CancellationToken ct) => Results.Ok(await s.GetAsync(ct)));
        c.MapPut("/settings", async (UpdateSettingsRequest req, SettingsService s, CancellationToken ct) => Results.Ok(await s.UpdateAsync(req, ct)));
        c.MapGet("/me", async (ConsultantService s, CancellationToken ct) => Results.Ok(await s.MeAsync(ct)));

        c.MapGet("/consultants", async (Guid? branchId, bool? includeInactive, ConsultantService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(branchId, includeInactive ?? false, ct)));
        c.MapPost("/consultants", async (CreateConsultantRequest req, ConsultantService s, CancellationToken ct) =>
        {
            var r = await s.CreateAsync(req, ct);
            return Results.Created($"/api/v1/consultation/consultants/{r.Id}", r);
        });
        c.MapGet("/consultants/{id:guid}", async (Guid id, ConsultantService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct)));
        c.MapPut("/consultants/{id:guid}", async (Guid id, UpdateConsultantRequest req, ConsultantService s, CancellationToken ct) =>
            Results.Ok(await s.UpdateAsync(id, req, ct)));

        c.MapGet("/slots", async (DateTime? from, DateTime? to, Guid? consultantId, Guid? branchId, SlotService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(from, to, consultantId, branchId, ct)));
        c.MapPost("/slots", async (CreateSlotRequest req, SlotService s, CancellationToken ct) =>
        {
            var r = await s.CreateAsync(req, ct);
            return Results.Created($"/api/v1/consultation/slots/{r.Id}", r);
        });
        c.MapPost("/slots/bulk", async (BulkSlotsRequest req, SlotService s, CancellationToken ct) => Results.Ok(await s.BulkAsync(req, ct)));
        c.MapDelete("/slots/{id:guid}", async (Guid id, SlotService s, CancellationToken ct) =>
        {
            await s.CancelAsync(id, ct);
            return Results.NoContent();
        });

        c.MapGet("/bookings", async ([AsParameters] PagingQuery q, string? scope, DateTime? from, DateTime? to, Guid? branchId, Guid? consultantId,
                string? status, BookingService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(q.ToPageRequest(), new BookingListQuery(scope, from, to, branchId, consultantId, status), ct)));
        c.MapGet("/bookings/{id:guid}", async (Guid id, BookingService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct)));
        foreach (var (route, action) in new[]
                 {
                     ("confirm", BookingAction.Confirm), ("cancel", BookingAction.Cancel), ("complete", BookingAction.Complete), ("no-show", BookingAction.NoShow),
                 })
        {
            c.MapPost($"/bookings/{{id:guid}}/{route}", async (Guid id, BookingActionRequest req, BookingService s, CancellationToken ct) =>
                Results.Ok(await s.ActAsync(id, action, req, ct)));
        }
        c.MapGet("/bookings/{id:guid}/notes", async (Guid id, BookingService s, CancellationToken ct) => Results.Ok(await s.NotesAsync(id, ct)));
        c.MapPost("/bookings/{id:guid}/notes", async (Guid id, CreateNoteRequest req, BookingService s, CancellationToken ct) =>
        {
            var r = await s.AddNoteAsync(id, req, ct);
            return Results.Created($"/api/v1/consultation/bookings/{id}/notes", r);
        });

        var pc = api.MapGroup("/portal/consultation").WithTags("Portal consultation").RequireAuthorization(Policies.TenantUser);
        pc.MapGet("/consultants", async (Guid studentId, PortalConsultationService s, CancellationToken ct) =>
            Results.Ok(await s.ConsultantsAsync(studentId, ct)));
        pc.MapGet("/slots", async (Guid studentId, Guid? consultantId, DateTime? from, DateTime? to, PortalConsultationService s, CancellationToken ct) =>
            Results.Ok(await s.SlotsAsync(studentId, consultantId, from, to, ct)));
        pc.MapGet("/bookings", async (Guid? studentId, PortalConsultationService s, CancellationToken ct) => Results.Ok(await s.ListAsync(studentId, ct)));
        pc.MapPost("/bookings", async (PortalBookRequest req, PortalConsultationService s, CancellationToken ct) =>
        {
            var r = await s.BookAsync(req, ct);
            return Results.Created($"/api/v1/portal/consultation/bookings/{r.Id}", r);
        });
        pc.MapPost("/bookings/{id:guid}/cancel", async (Guid id, PortalCancelRequest req, PortalConsultationService s, CancellationToken ct) =>
            Results.Ok(await s.CancelAsync(id, req, ct)));

        var ps = api.MapGroup("/portal/support").WithTags("Portal support").RequireAuthorization(Policies.TenantUser);
        ps.MapGet("/tickets", async (SupportService s, CancellationToken ct) => Results.Ok(await s.PortalListAsync(ct)));
        ps.MapPost("/tickets", async (CreateTicketRequest req, SupportService s, CancellationToken ct) =>
        {
            var r = await s.PortalCreateAsync(req, ct);
            return Results.Created($"/api/v1/portal/support/tickets/{r.Ticket.Id}", r);
        });
        ps.MapGet("/tickets/{id:guid}", async (Guid id, SupportService s, CancellationToken ct) => Results.Ok(await s.PortalGetAsync(id, ct)));
        ps.MapPost("/tickets/{id:guid}/messages", async (Guid id, TicketMessageRequest req, SupportService s, CancellationToken ct) =>
            Results.Ok(await s.PortalMessageAsync(id, req, ct)));
        ps.MapPost("/tickets/{id:guid}/close", async (Guid id, TicketRowVersionRequest req, SupportService s, CancellationToken ct) =>
            Results.Ok(await s.PortalCloseAsync(id, req, ct)));

        var st = api.MapGroup("/support").WithTags("Support").RequireAuthorization(Policies.TenantUser);
        st.MapGet("/tickets", async ([AsParameters] PagingQuery q, string? status, Guid? branchId, string? assignee, SupportService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(q.ToPageRequest(), new TicketListQuery(status, branchId, assignee), ct)));
        st.MapGet("/tickets/{id:guid}", async (Guid id, SupportService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct)));
        st.MapPost("/tickets/{id:guid}/messages", async (Guid id, TicketMessageRequest req, SupportService s, CancellationToken ct) =>
            Results.Ok(await s.MessageAsync(id, req, ct)));
        st.MapPut("/tickets/{id:guid}", async (Guid id, UpdateTicketRequest req, SupportService s, CancellationToken ct) =>
            Results.Ok(await s.UpdateAsync(id, req, ct)));
        return api;
    }
}

/// <summary>Records every read of confidential notes (DL-093): booking id and note count only.</summary>
internal sealed class AuditNotesAccessLog(IAuditWriter audit, ITenantContext tenant) : INotesAccessLog
{
    public Task NotesReadAsync(Guid bookingId, int count, CancellationToken ct = default) =>
        audit.WriteAsync([new AuditEntry("consultation.notes.read", "consultation.booking", bookingId.ToString(), tenant.TenantId)], ct);
}
