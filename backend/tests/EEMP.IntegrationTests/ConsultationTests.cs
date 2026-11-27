using System.Net;
using System.Text.Json.Nodes;
using EEMP.Infrastructure.Audit;
using EEMP.IntegrationTests.Infrastructure;
using EEMP.Modules.Consultation.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static EEMP.IntegrationTests.Infrastructure.Api;

namespace EEMP.IntegrationTests;

/// <summary>Movable clock for consultation rules only (token issuing/validation keep the real clock).</summary>
public sealed class TestConsultationClock : IConsultationClock
{
    private long _offsetTicks;
    public DateTime UtcNow => DateTime.UtcNow.AddTicks(Interlocked.Read(ref _offsetTicks));
    public void Advance(TimeSpan by) => Interlocked.Add(ref _offsetTicks, by.Ticks);
}

public sealed class ConsultationFactory : EempFactory
{
    public TestConsultationClock Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.AddSingleton<IConsultationClock>(Clock));
    }
}

[CollectionDefinition(Name)]
public sealed class ConsultationCollection : ICollectionFixture<ConsultationFactory>
{
    public const string Name = "consultation";
}

[Collection(ConsultationCollection.Name)]
public class ConsultationTests(ConsultationFactory factory)
{
    private readonly HttpClient _c = factory.CreateClient();
    private const string Pw = "Consult123";

    private static string Rv(JsonNode n) => n["rowVersion"]!.GetValue<string>();
    private static string Iso(DateTime d) => d.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

    private sealed record World(Session Admin, Session Manager, Session Consultant, Session Guardian, Session Student, Session Teacher,
        Guid North, Guid South, Guid ConsultantId, Guid Sara, Guid Ali);

    private async Task<World> WorldAsync()
    {
        var admin = await Demo(_c, "demo.admin");
        var items = (await Get(_c, "/api/v1/branches", admin.AccessToken))["items"]!.AsArray();
        Guid Branch(string code) => Id(items.First(b => b!["code"]!.GetValue<string>() == code)!);
        var manager = await Demo(_c, "branch.manager");
        var consultant = await Demo(_c, "consultant.one");
        var me = await Get(_c, "/api/v1/consultation/me", consultant.AccessToken);
        Assert.True(me["canConsult"]!.GetValue<bool>());
        var students = (await Get(_c, "/api/v1/students?pageSize=100", manager.AccessToken))["items"]!.AsArray();
        Guid Student(string number) => Id(students.First(s => s!["studentNumber"]!.GetValue<string>() == number)!);
        return new World(admin, manager, consultant, await Demo(_c, "guardian.one"), await Demo(_c, "student.one"), await Demo(_c, "teacher.one"),
            Branch("NORTH"), Branch("SOUTH"), Id(me["consultant"]!), Student("S0001"), Student("S0002"));
    }

    private async Task<Session> UserWithRoleAsync(string adminToken, string userName, string roleCode, string[] permissions, Guid? branchId)
    {
        var roles = (await Get(_c, "/api/v1/roles", adminToken)).AsArray();
        var role = roles.FirstOrDefault(r => r!["code"]!.GetValue<string>() == roleCode)
            ?? await Ok(await Send(_c, HttpMethod.Post, "/api/v1/roles", adminToken, new { code = roleCode, name = roleCode, permissions }), HttpStatusCode.Created);
        var users = (await Get(_c, $"/api/v1/users?search={userName}", adminToken))["items"]!.AsArray();
        var user = users.FirstOrDefault(u => u!["userName"]!.GetValue<string>() == userName)
            ?? await Ok(await Send(_c, HttpMethod.Post, "/api/v1/users", adminToken, new { userName, displayName = userName, password = Pw }), HttpStatusCode.Created);
        await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/users/{Id(user)}/assignments", adminToken, new[] { new { roleId = Id(role), branchId } }));
        return await Login(_c, "demo", userName, Pw);
    }

    private static DateTime _next = DateTime.MinValue;
    private static readonly object Gate = new();

    /// <summary>
    /// Next free start for the demo consultant at least <paramref name="hoursAhead"/> from the consultation clock. Slots are handed out
    /// sequentially (31 minutes apart) so tests never overlap each other, and they avoid 05:30–09:00 UTC where the seed puts its slots.
    /// </summary>
    private DateTime NextStart(double hoursAhead)
    {
        lock (Gate)
        {
            var min = factory.Clock.UtcNow.AddHours(hoursAhead);
            var c = _next > min ? _next : min;
            c = new DateTime(c.Year, c.Month, c.Day, c.Hour, c.Minute, 0, DateTimeKind.Utc).AddMinutes(1);
            var t = c.TimeOfDay;
            if (t >= TimeSpan.FromHours(5) && t < TimeSpan.FromHours(9)) c = c.Date.AddHours(9);
            if (c.TimeOfDay > TimeSpan.FromHours(23)) c = c.Date.AddDays(1).AddHours(9);
            _next = c.AddMinutes(31);
            return c;
        }
    }

    /// <summary>A fresh 30-minute in-person slot for the demo consultant in North, at least <paramref name="hoursAhead"/> from now.</summary>
    private Task<JsonNode> SlotAsync(World w, double hoursAhead) => ExactSlotAsync(w, NextStart(hoursAhead));

    private async Task<JsonNode> ExactSlotAsync(World w, DateTime start) =>
        await Ok(await Send(_c, HttpMethod.Post, "/api/v1/consultation/slots", w.Consultant.AccessToken, new
        {
            consultantId = w.ConsultantId, branchId = w.North, startsAt = Iso(start), endsAt = Iso(start.AddMinutes(30)), mode = "online",
        }), HttpStatusCode.Created);

    private Task<HttpResponseMessage> Book(Session s, Guid studentId, Guid slotId, string topic = "Study plan") =>
        Send(_c, HttpMethod.Post, "/api/v1/portal/consultation/bookings", s.AccessToken, new { studentId, slotId, topic, note = "Prefers mornings" });

    private async Task<JsonNode> StaffAct(Session s, Guid bookingId, string action, string? reason = null)
    {
        var detail = await Get(_c, $"/api/v1/consultation/bookings/{bookingId}", s.AccessToken);
        return await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/consultation/bookings/{bookingId}/{action}", s.AccessToken,
            new { rowVersion = Rv(detail["booking"]!), reason }));
    }

    [Fact]
    public async Task Guardian_books_consultant_confirms_and_completes_with_confidential_notes()
    {
        var w = await WorldAsync();
        var slot = await SlotAsync(w, 3);

        var consultants = (await Get(_c, $"/api/v1/portal/consultation/consultants?studentId={w.Sara}", w.Guardian.AccessToken)).AsArray();
        Assert.Contains(consultants, c => Id(c!) == w.ConsultantId);
        var slots = (await Get(_c, $"/api/v1/portal/consultation/slots?studentId={w.Sara}&consultantId={w.ConsultantId}", w.Guardian.AccessToken)).AsArray();
        Assert.Contains(slots, s => Id(s!) == Id(slot));

        var booking = await Ok(await Book(w.Guardian, w.Sara, Id(slot)), HttpStatusCode.Created);
        Assert.Equal("requested", booking["status"]!.GetValue<string>());
        // Three hours ahead is inside the 24-hour cancellation window: the family can no longer cancel online.
        Assert.False(booking["canCancel"]!.GetValue<bool>());
        Assert.Null(booking["notes"]);
        var bookingId = Id(booking);

        // The slot is no longer offered and shows the booking to staff.
        slots = (await Get(_c, $"/api/v1/portal/consultation/slots?studentId={w.Sara}", w.Guardian.AccessToken)).AsArray();
        Assert.DoesNotContain(slots, s => Id(s!) == Id(slot));
        // The student sees the booking made by the guardian.
        Assert.Contains((await Get(_c, "/api/v1/portal/consultation/bookings", w.Student.AccessToken)).AsArray(), b => Id(b!) == bookingId);

        // Notes cannot be written before confirmation; completion is impossible before the session.
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/consultation/bookings/{bookingId}/notes", w.Consultant.AccessToken, new { body = "x" }),
            HttpStatusCode.Conflict, "consultation.invalid_transition");
        var confirmed = await StaffAct(w.Consultant, bookingId, "confirm");
        Assert.Equal("confirmed", confirmed["booking"]!["status"]!.GetValue<string>());
        Assert.Contains("complete", confirmed["allowedActions"]!.AsArray().Select(a => a!.GetValue<string>()));
        var early = await Send(_c, HttpMethod.Post, $"/api/v1/consultation/bookings/{bookingId}/complete", w.Consultant.AccessToken,
            new { rowVersion = Rv(confirmed["booking"]!) });
        await Problem(early, HttpStatusCode.UnprocessableEntity, "consultation.session_not_started");

        var sessionStart = DateTime.Parse(slot["startsAt"]!.GetValue<string>()).ToUniversalTime();
        factory.Clock.Advance(sessionStart - factory.Clock.UtcNow + TimeSpan.FromMinutes(1));
        var done = await StaffAct(w.Consultant, bookingId, "complete");
        Assert.Equal("completed", done["booking"]!["status"]!.GetValue<string>());
        Assert.True(done["canWriteNotes"]!.GetValue<bool>());
        await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/consultation/bookings/{bookingId}/notes", w.Consultant.AccessToken,
            new { body = "Discussed exam anxiety; follow up in two weeks." }), HttpStatusCode.Created);
        var notes = (await Get(_c, $"/api/v1/consultation/bookings/{bookingId}/notes", w.Consultant.AccessToken)).AsArray();
        Assert.Equal("Discussed exam anxiety; follow up in two weeks.", Assert.Single(notes)!["body"]!.GetValue<string>());

        // Terminal: no further transitions.
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/consultation/bookings/{bookingId}/cancel", w.Consultant.AccessToken,
            new { rowVersion = Rv(done["booking"]!) }), HttpStatusCode.Conflict, "consultation.invalid_transition");

        // Branch manager sees the booking metadata but not the notes.
        var managerView = await Get(_c, $"/api/v1/consultation/bookings/{bookingId}", w.Manager.AccessToken);
        Assert.False(managerView["canReadNotes"]!.GetValue<bool>());
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/consultation/bookings/{bookingId}/notes", w.Manager.AccessToken),
            HttpStatusCode.Forbidden, "consultation.notes_forbidden");
        // TenantAdmin has no implicit access to consultation records at all.
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/consultation/bookings/{bookingId}", w.Admin.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/consultation/bookings/{bookingId}/notes", w.Admin.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Get, "/api/v1/consultation/bookings", w.Admin.AccessToken), HttpStatusCode.Forbidden, "authorization.forbidden");
        // Students, guardians and teachers have no staff access to bookings or notes.
        foreach (var s in new[] { w.Student, w.Guardian, w.Teacher })
            await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/consultation/bookings/{bookingId}/notes", s.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
        var portal = (await Get(_c, "/api/v1/portal/consultation/bookings", w.Student.AccessToken)).AsArray().First(b => Id(b!) == bookingId)!;
        Assert.Equal("completed", portal["status"]!.GetValue<string>());
        Assert.DoesNotContain("exam anxiety", portal.ToJsonString(), StringComparison.Ordinal);

        // An explicit notes.read grant (supervisor) in the branch reads the notes, and every read is audited.
        var supervisor = await UserWithRoleAsync(w.Admin.AccessToken, "cons.supervisor", "ConsSupervisor", ["consultation.notes.read"], w.North);
        Assert.Single((await Get(_c, $"/api/v1/consultation/bookings/{bookingId}/notes", supervisor.AccessToken)).AsArray());
        var southSupervisor = await UserWithRoleAsync(w.Admin.AccessToken, "cons.supervisor.s", "ConsSupervisor", ["consultation.notes.read"], w.South);
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/consultation/bookings/{bookingId}/notes", southSupervisor.AccessToken), HttpStatusCode.NotFound, "resource.not_found");

        await using var scope = factory.Services.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var reads = await audit.Events.Where(e => e.Action == "consultation.notes.read" && e.ResourceId == bookingId.ToString()).ToListAsync();
        Assert.True(reads.Count >= 2);
        Assert.All(reads, r => Assert.DoesNotContain("anxiety", r.Metadata ?? "", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Guardian_may_book_only_for_linked_students_in_their_branch()
    {
        var w = await WorldAsync();
        var slot = await SlotAsync(w, 4);
        // Ali is not linked to guardian.one; student.one is Sara, not Ali.
        await Problem(await Book(w.Guardian, w.Ali, Id(slot)), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Book(w.Student, w.Ali, Id(slot)), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/portal/consultation/slots?studentId={w.Ali}", w.Guardian.AccessToken),
            HttpStatusCode.NotFound, "resource.not_found");
        // Staff without a relationship cannot use the portal for a student either.
        await Problem(await Book(w.Manager, w.Sara, Id(slot)), HttpStatusCode.NotFound, "resource.not_found");

        // A consultant serving both branches publishes a South slot: Sara (North) cannot book it and does not see it.
        var tenantManager = await UserWithRoleAsync(w.Admin.AccessToken, "cons.manager.all", "ConsManagerAll", ["consultation.consultants.manage"], null);
        var profile = await Get(_c, $"/api/v1/consultation/consultants/{w.ConsultantId}", tenantManager.AccessToken);
        if (profile["branchIds"]!.AsArray().Count == 1)
            profile = await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/consultation/consultants/{w.ConsultantId}", tenantManager.AccessToken, new
            {
                displayName = profile["displayName"]!.GetValue<string>(), bio = "Counselor", branchIds = new[] { w.North, w.South }, isActive = true, rowVersion = Rv(profile),
            }));
        Assert.Equal(2, profile["branchIds"]!.AsArray().Count);
        var start = NextStart(5);
        var southSlot = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/consultation/slots", tenantManager.AccessToken, new
        {
            consultantId = w.ConsultantId, branchId = w.South, startsAt = Iso(start), endsAt = Iso(start.AddMinutes(30)), mode = "inPerson",
        }), HttpStatusCode.Created);
        Assert.DoesNotContain((await Get(_c, $"/api/v1/portal/consultation/slots?studentId={w.Sara}", w.Guardian.AccessToken)).AsArray(), s => Id(s!) == Id(southSlot));
        await Problem(await Book(w.Guardian, w.Sara, Id(southSlot)), HttpStatusCode.UnprocessableEntity, "consultation.branch_not_served");
        // The North branch manager cannot manage South slots, and the consultant cannot write South slots without consult there.
        await Problem(await Send(_c, HttpMethod.Delete, $"/api/v1/consultation/slots/{Id(southSlot)}", w.Manager.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
        var start2 = NextStart(5);
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/consultation/slots", w.Consultant.AccessToken, new
        {
            consultantId = w.ConsultantId, branchId = w.South, startsAt = Iso(start2), endsAt = Iso(start2.AddMinutes(30)), mode = "inPerson",
        }), HttpStatusCode.Forbidden, "authorization.forbidden_scope");
        await Ok(await Send(_c, HttpMethod.Delete, $"/api/v1/consultation/slots/{Id(southSlot)}", tenantManager.AccessToken), HttpStatusCode.NoContent);

        // Overlapping slots for the same consultant are rejected.
        var s1 = DateTime.Parse(slot["startsAt"]!.GetValue<string>()).ToUniversalTime();
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/consultation/slots", w.Consultant.AccessToken, new
        {
            consultantId = w.ConsultantId, branchId = w.North, startsAt = Iso(s1.AddMinutes(10)), endsAt = Iso(s1.AddMinutes(40)), mode = "inPerson",
        }), HttpStatusCode.Conflict, "consultation.slot_overlap");
        // Past slots are rejected.
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/consultation/slots", w.Consultant.AccessToken, new
        {
            consultantId = w.ConsultantId, branchId = w.North, startsAt = Iso(factory.Clock.UtcNow.AddHours(-2)), endsAt = Iso(factory.Clock.UtcNow.AddHours(-1.5)), mode = "inPerson",
        }), HttpStatusCode.UnprocessableEntity, "consultation.slot_in_past");
    }

    [Fact]
    public async Task Concurrent_bookings_for_one_slot_reserve_it_exactly_once()
    {
        var w = await WorldAsync();
        var slot = await SlotAsync(w, 30);
        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Book(i % 2 == 0 ? w.Guardian : w.Student, w.Sara, Id(slot), $"Race {i}")));
        Assert.Equal(1, attempts.Count(r => r.StatusCode == HttpStatusCode.Created));
        foreach (var r in attempts.Where(r => r.StatusCode != HttpStatusCode.Created))
            await Problem(r, HttpStatusCode.Conflict, "consultation.slot_unavailable");
        var staffSlots = (await Get(_c, $"/api/v1/consultation/slots?from={Iso(factory.Clock.UtcNow)}&to={Iso(factory.Clock.UtcNow.AddDays(60))}",
            w.Consultant.AccessToken)).AsArray();
        Assert.NotNull(staffSlots.First(s => Id(s!) == Id(slot))!["booking"]);

        // Cancelling frees the slot for a new booking; an active booking blocks deleting the slot.
        var booked = await Ok(attempts.First(r => r.StatusCode == HttpStatusCode.Created), HttpStatusCode.Created);
        await Problem(await Send(_c, HttpMethod.Delete, $"/api/v1/consultation/slots/{Id(slot)}", w.Consultant.AccessToken), HttpStatusCode.Conflict, "consultation.slot_has_bookings");
        await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/portal/consultation/bookings/{Id(booked)}/cancel", w.Guardian.AccessToken, new { rowVersion = Rv(booked) }));
        var again = await Ok(await Book(w.Student, w.Sara, Id(slot)), HttpStatusCode.Created);
        Assert.Equal("requested", again["status"]!.GetValue<string>());
        await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/portal/consultation/bookings/{Id(again)}/cancel", w.Student.AccessToken, new { rowVersion = Rv(again) }));
    }

    [Fact]
    public async Task Requester_cancellation_respects_the_window_but_staff_can_cancel()
    {
        var w = await WorldAsync();
        // Default window 24 h. Book a session ~30 h ahead, then move time so only ~5 h remain.
        var slot = await SlotAsync(w, 30);
        var booking = await Ok(await Book(w.Guardian, w.Sara, Id(slot)), HttpStatusCode.Created);
        var startsAt = DateTime.Parse(booking["startsAt"]!.GetValue<string>()).ToUniversalTime();
        factory.Clock.Advance(startsAt - factory.Clock.UtcNow - TimeSpan.FromHours(5));
        var mine = (await Get(_c, "/api/v1/portal/consultation/bookings", w.Guardian.AccessToken)).AsArray().First(b => Id(b!) == Id(booking))!;
        Assert.False(mine["canCancel"]!.GetValue<bool>());
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/portal/consultation/bookings/{Id(booking)}/cancel", w.Guardian.AccessToken,
            new { rowVersion = Rv(mine) }), HttpStatusCode.UnprocessableEntity, "consultation.cancellation_window_passed");
        // A stale rowversion from staff is a concurrency conflict.
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/consultation/bookings/{Id(booking)}/confirm", w.Manager.AccessToken,
            new { rowVersion = Convert.ToBase64String(new byte[8]) }), HttpStatusCode.Conflict, "concurrency.conflict");
        var cancelled = await StaffAct(w.Manager, Id(booking), "cancel", "Consultant unavailable");
        Assert.Equal("cancelled", cancelled["booking"]!["status"]!.GetValue<string>());
        Assert.Equal("staff", cancelled["booking"]!["cancelledBy"]!.GetValue<string>());

        // Booking-window rules: lead time and horizon (defaults: 0 h lead in the demo tenant, 30 days horizon).
        var far = await ExactSlotAsync(w, factory.Clock.UtcNow.Date.AddDays(40).AddHours(12));
        await Problem(await Book(w.Guardian, w.Sara, Id(far)), HttpStatusCode.UnprocessableEntity, "consultation.outside_booking_window");
    }

    [Fact]
    public async Task Settings_are_tenant_wide_configuration()
    {
        var w = await WorldAsync();
        var settings = await Get(_c, "/api/v1/consultation/settings", w.Guardian.AccessToken);
        Assert.Equal(24, settings["cancellationWindowHours"]!.GetValue<int>());
        var body = new { cancellationWindowHours = 12, bookingHorizonDays = 30, minLeadHours = 0, maxActiveBookingsPerStudent = 3, rowVersion = Rv(settings) };
        // A branch-scoped grant is not enough: settings are tenant-wide.
        await Problem(await Send(_c, HttpMethod.Put, "/api/v1/consultation/settings", w.Manager.AccessToken, body), HttpStatusCode.Forbidden, "authorization.forbidden_scope");
        await Problem(await Send(_c, HttpMethod.Put, "/api/v1/consultation/settings", w.Admin.AccessToken, body), HttpStatusCode.Forbidden, "authorization.forbidden_scope");
        var tenantManager = await UserWithRoleAsync(w.Admin.AccessToken, "cons.manager.all", "ConsManagerAll", ["consultation.consultants.manage"], null);
        var updated = await Ok(await Send(_c, HttpMethod.Put, "/api/v1/consultation/settings", tenantManager.AccessToken, body));
        Assert.Equal(12, updated["cancellationWindowHours"]!.GetValue<int>());
        await Problem(await Send(_c, HttpMethod.Put, "/api/v1/consultation/settings", tenantManager.AccessToken, body), HttpStatusCode.Conflict, "concurrency.conflict");
        await Problem(await Send(_c, HttpMethod.Put, "/api/v1/consultation/settings", tenantManager.AccessToken,
            new { cancellationWindowHours = -1, bookingHorizonDays = 0, minLeadHours = 0, maxActiveBookingsPerStudent = 3, rowVersion = Rv(updated) }),
            HttpStatusCode.BadRequest, "validation.failed");
        await Ok(await Send(_c, HttpMethod.Put, "/api/v1/consultation/settings", tenantManager.AccessToken,
            new { cancellationWindowHours = 24, bookingHorizonDays = 30, minLeadHours = 0, maxActiveBookingsPerStudent = 3, rowVersion = Rv(updated) }));
    }

    [Fact]
    public async Task Tenant_and_branch_isolation_for_bookings_slots_and_tickets()
    {
        var w = await WorldAsync();
        var slot = await SlotAsync(w, 8);
        var booking = await Ok(await Book(w.Guardian, w.Sara, Id(slot)), HttpStatusCode.Created);
        var ticket = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/portal/support/tickets", w.Guardian.AccessToken,
            new { studentId = w.Sara, subject = "Isolation", category = "general", body = "Hello" }), HttpStatusCode.Created);
        var ticketId = Id(ticket["ticket"]!);

        // South-scoped staff do not see North records.
        var south = await UserWithRoleAsync(w.Admin.AccessToken, "cons.south", "ConsSouthStaff",
            ["consultation.bookings.read", "consultation.bookings.manage", "support.tickets.read", "support.tickets.manage"], w.South);
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/consultation/bookings/{Id(booking)}", south.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
        Assert.DoesNotContain((await Get(_c, "/api/v1/consultation/bookings?pageSize=100", south.AccessToken))["items"]!.AsArray(), b => Id(b!) == Id(booking));
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/support/tickets/{ticketId}", south.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
        Assert.DoesNotContain((await Get(_c, "/api/v1/support/tickets?pageSize=100", south.AccessToken))["items"]!.AsArray(), t => Id(t!) == ticketId);
        Assert.DoesNotContain((await Get(_c, $"/api/v1/consultation/slots?to={Iso(factory.Clock.UtcNow.AddDays(30))}&from={Iso(factory.Clock.UtcNow)}", south.AccessToken)).AsArray(),
            s => Id(s!) == Id(slot));

        // Another tenant cannot reach any of it by id.
        var platform = await Login(_c, null, "platform.admin", EempFactory.PlatformPassword);
        var slug = $"c{Guid.NewGuid():N}"[..12];
        await Ok(await Send(_c, HttpMethod.Post, "/api/v1/platform/tenants", platform.AccessToken, new
        {
            slug, name = "Other Institute", adminUser = new { userName = "other.admin", displayName = "Other Admin", password = "OtherAdmin1" },
        }), HttpStatusCode.Created);
        var other = await Login(_c, slug, "other.admin", "OtherAdmin1");
        var otherRoles = (await Get(_c, "/api/v1/roles", other.AccessToken)).AsArray();
        // Give the other tenant's admin every consultation/support permission tenant-wide in their own tenant.
        var all = new[] { "consultation.consultants.manage", "consultation.bookings.read", "consultation.bookings.manage", "consultation.notes.read",
            "support.tickets.read", "support.tickets.manage" };
        var role = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/roles", other.AccessToken, new { code = "AllCons", name = "All", permissions = all }), HttpStatusCode.Created);
        var otherUser = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/users", other.AccessToken, new { userName = "other.cons", displayName = "x", password = Pw }), HttpStatusCode.Created);
        await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/users/{Id(otherUser)}/assignments", other.AccessToken, new[] { new { roleId = Id(role), branchId = (Guid?)null } }));
        var otherCons = await Login(_c, slug, "other.cons", Pw);
        Assert.NotEmpty(otherRoles);
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/consultation/bookings/{Id(booking)}", otherCons.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/consultation/bookings/{Id(booking)}/notes", otherCons.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/consultation/consultants/{w.ConsultantId}", otherCons.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Delete, $"/api/v1/consultation/slots/{Id(slot)}", otherCons.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/support/tickets/{ticketId}", otherCons.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
        Assert.Equal(0, (await Get(_c, "/api/v1/consultation/bookings", otherCons.AccessToken))["totalCount"]!.GetValue<int>());
        Assert.Equal(0, (await Get(_c, "/api/v1/support/tickets", otherCons.AccessToken))["totalCount"]!.GetValue<int>());
        // A cross-tenant slot id cannot be booked either (the other tenant has no linked students, so the student check fails first).
        await Problem(await Book(other, w.Sara, Id(slot)), HttpStatusCode.NotFound, "resource.not_found");

        await StaffAct(w.Manager, Id(booking), "cancel");
    }

    [Fact]
    public async Task Support_ticket_round_trip_and_access()
    {
        var w = await WorldAsync();
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/portal/support/tickets", w.Guardian.AccessToken,
            new { studentId = w.Ali, subject = "Not mine", category = "general", body = "x" }), HttpStatusCode.NotFound, "resource.not_found");
        var created = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/portal/support/tickets", w.Guardian.AccessToken,
            new { studentId = w.Sara, subject = "Report card question", category = "academic", body = "When are term grades published?" }), HttpStatusCode.Created);
        var t = created["ticket"]!;
        var id = Id(t);
        Assert.Equal("open", t["status"]!.GetValue<string>());
        Assert.True(t["number"]!.GetValue<int>() >= 1);

        // Only the requester sees it in the portal (the student did not create it).
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/portal/support/tickets/{id}", w.Student.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
        // Staff without support permissions: no inbox, no ticket.
        await Problem(await Send(_c, HttpMethod.Get, "/api/v1/support/tickets", w.Teacher.AccessToken), HttpStatusCode.Forbidden, "authorization.forbidden");
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/support/tickets/{id}", w.Teacher.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Get, "/api/v1/support/tickets", w.Admin.AccessToken), HttpStatusCode.Forbidden, "authorization.forbidden");

        // Branch manager: inbox, internal note, public reply, assignment.
        Assert.Contains((await Get(_c, "/api/v1/support/tickets?status=open&pageSize=100", w.Manager.AccessToken))["items"]!.AsArray(), x => Id(x!) == id);
        await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/support/tickets/{id}/messages", w.Manager.AccessToken, new { body = "Internal: check with registrar", isInternal = true }));
        var replied = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/support/tickets/{id}/messages", w.Manager.AccessToken, new { body = "Next Saturday.", isInternal = false }));
        Assert.Equal("waitingOnRequester", replied["ticket"]!["status"]!.GetValue<string>());
        Assert.Equal(3, replied["messages"]!.AsArray().Count);
        var managerId = Guid.Parse(w.Manager.User["id"]!.GetValue<string>());
        var assigned = await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/support/tickets/{id}", w.Manager.AccessToken,
            new { status = "inProgress", priority = "high", assigneeUserId = managerId, rowVersion = Rv(replied["ticket"]!) }));
        Assert.Equal("North Branch Manager", assigned["ticket"]!["assigneeName"]!.GetValue<string>());
        Assert.Contains((await Get(_c, "/api/v1/support/tickets?assignee=me", w.Manager.AccessToken))["items"]!.AsArray(), x => Id(x!) == id);
        await Problem(await Send(_c, HttpMethod.Put, $"/api/v1/support/tickets/{id}", w.Manager.AccessToken,
            new { status = "inProgress", priority = "high", assigneeUserId = Guid.NewGuid(), rowVersion = Rv(assigned["ticket"]!) }), HttpStatusCode.BadRequest, "validation.failed");

        // The requester sees the public reply only, without the staff member's name, and replies (reopens).
        var portalView = await Get(_c, $"/api/v1/portal/support/tickets/{id}", w.Guardian.AccessToken);
        var msgs = portalView["messages"]!.AsArray();
        Assert.Equal(2, msgs.Count);
        Assert.DoesNotContain(msgs, m => m!["isInternal"]!.GetValue<bool>());
        Assert.DoesNotContain("registrar", portalView.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal("", msgs[1]!["authorName"]!.GetValue<string>());
        Assert.Null(portalView["ticket"]!["assigneeUserId"]);
        var back = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/portal/support/tickets/{id}/messages", w.Guardian.AccessToken, new { body = "Thanks!" }));
        Assert.Equal("inProgress", back["ticket"]!["status"]!.GetValue<string>());

        // Requester closes; closed is terminal for messages.
        var closed = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/portal/support/tickets/{id}/close", w.Guardian.AccessToken, new { rowVersion = Rv(back["ticket"]!) }));
        Assert.Equal("closed", closed["ticket"]!["status"]!.GetValue<string>());
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/portal/support/tickets/{id}/messages", w.Guardian.AccessToken, new { body = "One more" }),
            HttpStatusCode.Conflict, "support.ticket_closed");
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/support/tickets/{id}/messages", w.Manager.AccessToken, new { body = "x", isInternal = false }),
            HttpStatusCode.Conflict, "support.ticket_closed");
        Assert.Contains((await Get(_c, "/api/v1/portal/support/tickets", w.Guardian.AccessToken)).AsArray(), x => Id(x!) == id);
        Assert.DoesNotContain((await Get(_c, "/api/v1/portal/support/tickets", w.Student.AccessToken)).AsArray(), x => Id(x!) == id);
    }
}
