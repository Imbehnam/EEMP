using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using EEMP.IntegrationTests.Infrastructure;
using EEMP.Modules.Workflow.Application;
using EEMP.Modules.Workflow.Contracts;
using EEMP.Modules.Workflow.Domain;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static EEMP.IntegrationTests.Infrastructure.Api;

namespace EEMP.IntegrationTests;

/// <summary>Stands in for a module that owns workflow subjects ("testmod"); records deliveries and can fail on demand.</summary>
public sealed class TestWorkflowHandler : IWorkflowEventHandler
{
    public string SubjectModule => "testmod";
    public ConcurrentQueue<WorkflowEvent> Calls { get; } = new();
    public ConcurrentDictionary<Guid, WorkflowEvent> Applied { get; } = new();
    private int _failNext;

    public void FailNextDelivery() => Interlocked.Exchange(ref _failNext, 1);

    public Task HandleAsync(WorkflowEvent workflowEvent, CancellationToken ct)
    {
        Calls.Enqueue(workflowEvent);
        if (Interlocked.Decrement(ref _failNext) >= 0) throw new InvalidOperationException("simulated handler failure");
        Interlocked.Exchange(ref _failNext, 0);
        Applied.TryAdd(workflowEvent.EventId, workflowEvent); // idempotent by EventId
        return Task.CompletedTask;
    }

    public int AppliedFor(Guid instanceId) => Applied.Values.Count(e => e.InstanceId == instanceId);
    public int CallsFor(Guid instanceId) => Calls.Count(e => e.InstanceId == instanceId);
}

public sealed class WorkflowFactory : EempFactory
{
    public TestWorkflowHandler Handler { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(s => s.AddSingleton<IWorkflowEventHandler>(Handler));
    }
}

[CollectionDefinition(Name)]
public sealed class WorkflowCollection : ICollectionFixture<WorkflowFactory>
{
    public const string Name = "workflow";
}

[Collection(WorkflowCollection.Name)]
public class WorkflowTests(WorkflowFactory factory)
{
    private readonly HttpClient _c = factory.CreateClient();
    private const string Approve = "workflow.tasks.approve";

    private sealed record Actors(Session Admin, Session Manager, Session Teacher, Session Approver2, Session SouthApprover, Guid TenantId, Guid North, Guid South);

    private static Guid UserId(Session s) => Guid.Parse(s.User["id"]!.GetValue<string>());

    private async Task<Actors> ActorsAsync()
    {
        var admin = await Demo(_c, "demo.admin");
        var tenantId = Guid.Parse(admin.User["tenantId"]!.GetValue<string>());
        var items = (await Get(_c, "/api/v1/branches", admin.AccessToken))["items"]!.AsArray();
        Guid Find(string code) => Id(items.First(b => b!["code"]!.GetValue<string>() == code)!);
        var (north, south) = (Find("NORTH"), Find("SOUTH"));
        var approver2 = await UserWithRoleAsync(admin.AccessToken, "wf.approver2", north);
        var southApprover = await UserWithRoleAsync(admin.AccessToken, "wf.south", south);
        return new Actors(admin, await Demo(_c, "branch.manager"), await Demo(_c, "teacher.one"), approver2, southApprover, tenantId, north, south);
    }

    private async Task<Session> UserWithRoleAsync(string adminToken, string userName, Guid branchId)
    {
        var roles = (await Get(_c, "/api/v1/roles", adminToken)).AsArray();
        var role = roles.FirstOrDefault(r => r!["code"]!.GetValue<string>() == "WfApprover")
            ?? await Ok(await Send(_c, HttpMethod.Post, "/api/v1/roles", adminToken, new { code = "WfApprover", name = "Workflow approver", permissions = new[] { Approve } }), HttpStatusCode.Created);
        var users = (await Get(_c, $"/api/v1/users?search={userName}", adminToken))["items"]!.AsArray();
        var user = users.FirstOrDefault()
            ?? await Ok(await Send(_c, HttpMethod.Post, "/api/v1/users", adminToken, new { userName, displayName = userName, password = "Approver123" }), HttpStatusCode.Created);
        await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/users/{Id(user)}/assignments", adminToken, new[] { new { roleId = Id(role), branchId = (Guid?)branchId } }));
        return await Login(_c, "demo", userName, "Approver123");
    }

    /// <summary>Creates and publishes a "testmod/item" definition with the given steps; returns its key.</summary>
    private async Task<string> PublishedDefinitionAsync(string adminToken, object[] steps, bool distinct = true)
    {
        var key = $"t-{Guid.NewGuid():N}"[..20];
        var def = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/workflow/definitions", adminToken,
            new { key, name = "Test approval", subjectModule = "testmod", subjectType = "item", requireDistinctApprovers = distinct, steps }), HttpStatusCode.Created);
        await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/workflow/definitions/{Id(def)}/versions/1/publish", adminToken, new { rowVersion = Rv(def) }));
        return key;
    }

    private async Task<WorkflowInstanceInfo> StartAsync(Guid tenantId, string key, Guid requester, Guid? branchId, Guid? subjectId = null, string? idempotencyKey = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        using var _ = scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginTenantScope(tenantId);
        var sid = subjectId ?? Guid.NewGuid();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowService>().StartAsync(
            new StartWorkflowCommand(key, "testmod", "item", sid, branchId, "Item approval", idempotencyKey ?? $"item:{sid}", requester));
    }

    private static string Rv(JsonNode n) => n["rowVersion"]!.GetValue<string>();

    private Task<JsonNode> Detail(Guid id, Session s) => Get(_c, $"/api/v1/workflow/instances/{id}", s.AccessToken);

    private async Task<HttpResponseMessage> Act(Guid id, string action, Session s, string? comment = null, string? requestId = null, string? rowVersion = null)
    {
        rowVersion ??= Rv(await Detail(id, s));
        return await Send(_c, HttpMethod.Post, $"/api/v1/workflow/instances/{id}/{action}", s.AccessToken, new { rowVersion, comment, requestId });
    }

    private async Task<List<Guid>> Inbox(Session s) =>
        (await Get(_c, "/api/v1/workflow/inbox?pageSize=100", s.AccessToken))["items"]!.AsArray().Select(i => Id(i!)).ToList();

    [Fact]
    public async Task Definitions_are_versioned_and_published_versions_are_immutable()
    {
        var admin = (await Demo(_c, "demo.admin")).AccessToken;
        var manager = (await Demo(_c, "branch.manager")).AccessToken;

        var seeded = (await Get(_c, "/api/v1/workflow/definitions", admin)).AsArray().First(d => d!["key"]!.GetValue<string>() == "discount-approval")!;
        Assert.Equal(1, seeded["publishedVersion"]!.GetValue<int>());
        Assert.Equal("finance", seeded["subjectModule"]!.GetValue<string>());

        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/workflow/definitions", manager, new { key = "x-def", name = "X", subjectModule = "testmod", subjectType = "item", steps = new[] { new { name = "A", permission = Approve } } }),
            HttpStatusCode.Forbidden, "authorization.forbidden");
        var bad = await Send(_c, HttpMethod.Post, "/api/v1/workflow/definitions", admin, new { key = "Bad Key", name = "X", subjectModule = "testmod", subjectType = "item", steps = new[] { new { name = "A", permission = "nope.perm", slaHours = 0 } } });
        await Problem(bad, HttpStatusCode.BadRequest, "validation.failed");
        var errors = JsonNode.Parse(await bad.Content.ReadAsStringAsync())!["errors"]!;
        Assert.Equal("validation.invalid_format", errors["key"]![0]!.GetValue<string>());
        Assert.Equal("validation.unknown_permission", errors["steps[0].permission"]![0]!.GetValue<string>());
        Assert.Equal("validation.invalid_value", errors["steps[0].slaHours"]![0]!.GetValue<string>());
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/workflow/definitions", admin, new { key = "x-assignee", name = "X", subjectModule = "testmod", subjectType = "item", steps = new[] { new { name = "A", permission = Approve, assigneeUserId = Guid.NewGuid() } } }),
            HttpStatusCode.BadRequest, "validation.failed");

        var key = $"v-{Guid.NewGuid():N}"[..20];
        var def = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/workflow/definitions", admin,
            new { key, name = "Versioned", subjectModule = "testmod", subjectType = "item", steps = new[] { new { name = "A", permission = Approve, slaHours = 24 } } }), HttpStatusCode.Created);
        var id = Id(def);
        Assert.Equal("draft", def["versions"]![0]!["status"]!.GetValue<string>());
        Assert.Null(def["publishedVersion"]);
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/workflow/definitions", admin, new { key, name = "Dup", subjectModule = "testmod", subjectType = "item", steps = new[] { new { name = "A", permission = Approve } } }),
            HttpStatusCode.Conflict, "workflow.definition_key_taken");

        // Starting needs a published version.
        var t = await ActorsAsync();
        var notPublished = await Assert.ThrowsAsync<BusinessRuleException>(() => StartAsync(t.TenantId, key, UserId(t.Teacher), t.North));
        Assert.Equal(WorkflowErrors.DefinitionNotPublished, notPublished.Code);

        var published = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/workflow/definitions/{id}/versions/1/publish", admin, new { rowVersion = Rv(def) }));
        Assert.Equal(1, published["publishedVersion"]!.GetValue<int>());
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/workflow/definitions/{id}/versions/1/publish", admin, new { rowVersion = Rv(published) }),
            HttpStatusCode.Conflict, "workflow.version_not_draft");
        await Problem(await Send(_c, HttpMethod.Put, $"/api/v1/workflow/definitions/{id}/versions/1", admin,
            new { rowVersion = Rv(published["versions"]![0]!), steps = new[] { new { name = "B", permission = Approve } } }), HttpStatusCode.Conflict, "workflow.version_not_draft");

        var running = await StartAsync(t.TenantId, key, UserId(t.Teacher), t.North);
        Assert.Equal(1, running.VersionNo);

        var v2 = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/workflow/definitions/{id}/versions", admin), HttpStatusCode.Created);
        Assert.Equal("draft", v2["versions"]![1]!["status"]!.GetValue<string>());
        Assert.Equal("A", v2["versions"]![1]!["steps"]![0]!["name"]!.GetValue<string>());
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/workflow/definitions/{id}/versions", admin), HttpStatusCode.Conflict, "workflow.draft_exists");
        var edited = await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/workflow/definitions/{id}/versions/2", admin, new
        {
            rowVersion = Rv(v2["versions"]![1]!), requireDistinctApprovers = false,
            steps = new object[] { new { name = "First", permission = Approve }, new { name = "Second", permission = Approve, allowReturn = false } },
        }));
        Assert.Equal(2, edited["versions"]![1]!["steps"]!.AsArray().Count);
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/workflow/definitions/{id}/versions/2/publish", admin, new { rowVersion = Rv(def) }),
            HttpStatusCode.Conflict, "concurrency.conflict");
        var p2 = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/workflow/definitions/{id}/versions/2/publish", admin, new { rowVersion = Rv(edited) }));
        Assert.Equal(2, p2["publishedVersion"]!.GetValue<int>());
        Assert.Equal("retired", p2["versions"]![0]!["status"]!.GetValue<string>());

        // The running instance stays pinned to version 1 (one step); new instances use version 2.
        var pinned = await Detail(running.Id, t.Teacher);
        Assert.Equal(1, pinned["versionNo"]!.GetValue<int>());
        Assert.Equal(2, (await StartAsync(t.TenantId, key, UserId(t.Teacher), t.North)).VersionNo);

        // Deactivated definitions cannot start new instances.
        var off = await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/workflow/definitions/{id}", admin, new { name = "Versioned", isActive = false, rowVersion = Rv(p2) }));
        Assert.False(off["isActive"]!.GetValue<bool>());
        var inactive = await Assert.ThrowsAsync<BusinessRuleException>(() => StartAsync(t.TenantId, key, UserId(t.Teacher), t.North));
        Assert.Equal(WorkflowErrors.DefinitionInactive, inactive.Code);
    }

    [Fact]
    public async Task Two_step_approval_applies_maker_checker_and_distinct_approvers_and_completes_once()
    {
        var t = await ActorsAsync();
        var key = await PublishedDefinitionAsync(t.Admin.AccessToken, [new { name = "Review", permission = Approve, slaHours = 24 }, new { name = "Final", permission = Approve }]);
        var started = await StartAsync(t.TenantId, key, UserId(t.Teacher), t.North);
        Assert.Equal("pending", started.Status);
        Assert.NotNull(started.CurrentStepDueAt);

        // The requester sees it under "mine" but has no approve action; approvers see it in the inbox.
        var mine = (await Get(_c, "/api/v1/workflow/instances?scope=mine", t.Teacher.AccessToken))["items"]!.AsArray().Select(i => Id(i!)).ToList();
        Assert.Contains(started.Id, mine);
        Assert.DoesNotContain(started.Id, await Inbox(t.Teacher));
        await Problem(await Act(started.Id, "approve", t.Teacher), HttpStatusCode.Forbidden, "workflow.not_eligible");
        Assert.Contains(started.Id, await Inbox(t.Manager));
        Assert.Contains(started.Id, await Inbox(t.Approver2));
        var detail = await Detail(started.Id, t.Manager);
        Assert.Equal(["approve", "reject", "return", "cancel"], detail["allowedActions"]!.AsArray().Select(a => a!.GetValue<string>()).ToArray());
        Assert.False(detail["currentStep"]!["isOverdue"]!.GetValue<bool>());

        var afterFirst = await Ok(await Act(started.Id, "approve", t.Manager, "looks fine"));
        Assert.Equal(2, afterFirst["currentStep"]!["order"]!.GetValue<int>());
        Assert.DoesNotContain(afterFirst["allowedActions"]!.AsArray(), a => a!.GetValue<string>() == "approve");

        // Distinct approvers: the first approver cannot also approve step 2.
        Assert.DoesNotContain(started.Id, await Inbox(t.Manager));
        await Problem(await Act(started.Id, "approve", t.Manager), HttpStatusCode.Forbidden, "workflow.approver_already_acted");

        var done = await Ok(await Act(started.Id, "approve", t.Approver2));
        Assert.Equal("approved", done["status"]!.GetValue<string>());
        Assert.NotNull(done["completedAt"]);
        var history = done["history"]!.AsArray();
        Assert.Equal(["started", "approved", "approved"], history.Select(h => h!["action"]!.GetValue<string>()).ToArray());
        Assert.Equal(UserId(t.Approver2), Guid.Parse(history[2]!["actorUserId"]!.GetValue<string>()));
        Assert.Equal("looks fine", history[1]!["comment"]!.GetValue<string>());

        // Completion callback delivered exactly once, after commit.
        Assert.Equal(1, factory.Handler.AppliedFor(started.Id));
        var evt = factory.Handler.Applied.Values.Single(e => e.InstanceId == started.Id);
        Assert.Equal((WorkflowEventTypes.Completed, WorkflowOutcomes.Approved), (evt.EventType, evt.Outcome));
        Assert.Equal(t.TenantId, evt.TenantId);

        // Terminal: nothing else is allowed.
        await Problem(await Act(started.Id, "cancel", t.Teacher), HttpStatusCode.Conflict, "workflow.invalid_transition");

        // Maker-checker: a requester who holds the approve permission still cannot decide on their own request.
        var own = await StartAsync(t.TenantId, key, UserId(t.Manager), t.North);
        Assert.DoesNotContain(own.Id, await Inbox(t.Manager));
        await Problem(await Act(own.Id, "approve", t.Manager), HttpStatusCode.Forbidden, "workflow.self_approval_forbidden");
        await Problem(await Act(own.Id, "reject", t.Manager, "no"), HttpStatusCode.Forbidden, "workflow.self_approval_forbidden");
    }

    [Fact]
    public async Task Instances_are_isolated_by_branch_and_tenant()
    {
        var t = await ActorsAsync();
        var key = await PublishedDefinitionAsync(t.Admin.AccessToken, [new { name = "Review", permission = Approve }]);
        var north = await StartAsync(t.TenantId, key, UserId(t.Teacher), t.North);
        var tenantLevel = await StartAsync(t.TenantId, key, UserId(t.Teacher), null);

        // A south-branch approver neither sees nor acts on a north instance.
        Assert.DoesNotContain(north.Id, await Inbox(t.SouthApprover));
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/workflow/instances/{north.Id}", t.SouthApprover.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/workflow/instances/{north.Id}/approve", t.SouthApprover.AccessToken, new { rowVersion = "AAAAAAAAAAA=" }),
            HttpStatusCode.NotFound, "resource.not_found");

        // Tenant-level instances need a tenant-wide grant: a branch-scoped manager does not get them.
        Assert.DoesNotContain(tenantLevel.Id, await Inbox(t.Manager));
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/workflow/instances/{tenantLevel.Id}", t.Manager.AccessToken), HttpStatusCode.NotFound, "resource.not_found");

        // The tenant administrator has no implicit access to instances.
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/workflow/instances/{north.Id}", t.Admin.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Get, "/api/v1/workflow/instances?scope=all", t.Admin.AccessToken), HttpStatusCode.Forbidden, "authorization.forbidden");
        Assert.Empty(await Inbox(t.Admin));

        // Oversight list (workflow.instances.read) is limited to the manager's branch.
        var all = (await Get(_c, "/api/v1/workflow/instances?scope=all&pageSize=100", t.Manager.AccessToken))["items"]!.AsArray();
        Assert.Contains(all, i => Id(i!) == north.Id);
        Assert.All(all, i => Assert.Equal(t.North, Guid.Parse(i!["branchId"]!.GetValue<string>())));

        // Another tenant cannot see demo instances or definitions.
        var platform = await Login(_c, null, "platform.admin", EempFactory.PlatformPassword);
        var slug = $"w{Guid.NewGuid():N}"[..12];
        await Ok(await Send(_c, HttpMethod.Post, "/api/v1/platform/tenants", platform.AccessToken, new
        {
            slug, name = "Other Institute", adminUser = new { userName = "other.admin", displayName = "Other Admin", password = "OtherAdmin1" },
        }), HttpStatusCode.Created);
        var other = await Login(_c, slug, "other.admin", "OtherAdmin1");
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/workflow/instances/{north.Id}", other.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
        Assert.Empty((await Get(_c, "/api/v1/workflow/definitions", other.AccessToken)).AsArray());
        Assert.Empty(await Inbox(other));

        // An instance cannot be started on a branch that does not exist in the tenant.
        await Assert.ThrowsAsync<ValidationException>(() => StartAsync(t.TenantId, key, UserId(t.Teacher), Guid.NewGuid()));
    }

    [Fact]
    public async Task Return_resubmit_and_cancel_notify_the_owning_module()
    {
        var t = await ActorsAsync();
        var key = await PublishedDefinitionAsync(t.Admin.AccessToken, [new { name = "Review", permission = Approve }]);
        var i = await StartAsync(t.TenantId, key, UserId(t.Teacher), t.North);

        await Problem(await Act(i.Id, "return", t.Manager), HttpStatusCode.BadRequest, "validation.failed");
        var returned = await Ok(await Act(i.Id, "return", t.Manager, "add the invoice number"));
        Assert.Equal("returned", returned["status"]!.GetValue<string>());
        Assert.Null(returned["currentStep"]);
        Assert.Contains(factory.Handler.Applied.Values, e => e.InstanceId == i.Id && e.EventType == WorkflowEventTypes.Returned);
        Assert.DoesNotContain(i.Id, await Inbox(t.Manager));

        await Problem(await Act(i.Id, "resubmit", t.Manager), HttpStatusCode.Forbidden, "workflow.not_eligible");
        var again = await Ok(await Act(i.Id, "resubmit", t.Teacher, "added"));
        Assert.Equal("pending", again["status"]!.GetValue<string>());
        Assert.Equal(2, again["round"]!.GetValue<int>());
        Assert.Contains(i.Id, await Inbox(t.Manager));

        // Another user cannot cancel; the requester can.
        await Problem(await Act(i.Id, "cancel", t.Approver2), HttpStatusCode.Forbidden, "workflow.not_eligible");
        var cancelled = await Ok(await Act(i.Id, "cancel", t.Teacher));
        Assert.Equal("cancelled", cancelled["status"]!.GetValue<string>());
        Assert.Contains(factory.Handler.Applied.Values, e => e.InstanceId == i.Id && e.Outcome == WorkflowOutcomes.Cancelled);
        Assert.Equal(["started", "returned", "resubmitted", "cancelled"],
            cancelled["history"]!.AsArray().Select(h => h!["action"]!.GetValue<string>()).ToArray());

        // Steps without allowReturn cannot be returned; the branch manager (workflow.instances.manage) can cancel any branch instance.
        var noReturn = await PublishedDefinitionAsync(t.Admin.AccessToken, [new { name = "Review", permission = Approve, allowReturn = false }]);
        var j = await StartAsync(t.TenantId, noReturn, UserId(t.Teacher), t.North);
        await Problem(await Act(j.Id, "return", t.Manager, "x"), HttpStatusCode.UnprocessableEntity, "workflow.return_not_allowed");
        Assert.Equal("cancelled", (await Ok(await Act(j.Id, "cancel", t.Manager)))["status"]!.GetValue<string>());

        // System cancel by the owning module is a no-op on finished instances.
        var k = await StartAsync(t.TenantId, noReturn, UserId(t.Teacher), t.North);
        await using (var scope = factory.Services.CreateAsyncScope())
        using (scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginTenantScope(t.TenantId))
        {
            var wf = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
            await wf.CancelAsync(k.Id, "testmod", "subject deleted");
            await wf.CancelAsync(k.Id, "testmod", "again");
            Assert.Equal("cancelled", (await wf.FindAsync(k.Id))!.Status);
            Assert.Null(await wf.FindActiveAsync("testmod", "item", k.SubjectId));
            await Assert.ThrowsAsync<NotFoundException>(() => wf.CancelAsync(k.Id, "othermod", null));
        }
        Assert.Equal(1, factory.Handler.AppliedFor(k.Id));
    }

    [Fact]
    public async Task Concurrent_and_retried_commands_do_not_corrupt_state()
    {
        var t = await ActorsAsync();
        var key = await PublishedDefinitionAsync(t.Admin.AccessToken, [new { name = "Review", permission = Approve }, new { name = "Final", permission = Approve }], distinct: false);
        var i = await StartAsync(t.TenantId, key, UserId(t.Teacher), t.North);
        var stale = Rv(await Detail(i.Id, t.Manager));

        // Two approvers act on the same version of the instance: the second is rejected by the rowversion check.
        await Ok(await Act(i.Id, "approve", t.Manager, rowVersion: stale, requestId: "req-1"));
        await Problem(await Act(i.Id, "approve", t.Approver2, rowVersion: stale), HttpStatusCode.Conflict, "concurrency.conflict");

        // A retry of the same command (same requestId) returns the current state without applying it twice.
        var retried = await Ok(await Act(i.Id, "approve", t.Manager, rowVersion: stale, requestId: "req-1"));
        Assert.Equal(2, retried["currentStep"]!["order"]!.GetValue<int>());
        Assert.Equal(2, retried["history"]!.AsArray().Count);

        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/workflow/instances/{i.Id}/approve", t.Manager.AccessToken, new { comment = "x" }),
            HttpStatusCode.BadRequest, "validation.failed");
    }

    [Fact]
    public async Task Starting_is_idempotent_and_completion_delivery_retries_until_the_handler_succeeds()
    {
        var t = await ActorsAsync();
        var key = await PublishedDefinitionAsync(t.Admin.AccessToken, [new { name = "Review", permission = Approve }]);
        var subject = Guid.NewGuid();
        var first = await StartAsync(t.TenantId, key, UserId(t.Teacher), t.North, subject, "item:retry-1");
        var retry = await StartAsync(t.TenantId, key, UserId(t.Teacher), t.North, subject, "item:retry-1");
        Assert.Equal(first.Id, retry.Id);
        var mismatch = await Assert.ThrowsAsync<ConflictException>(() => StartAsync(t.TenantId, key, UserId(t.Teacher), t.North, Guid.NewGuid(), "item:retry-1"));
        Assert.Equal(WorkflowErrors.SubjectMismatch, mismatch.Code);
        var active = await Assert.ThrowsAsync<ConflictException>(() => StartAsync(t.TenantId, key, UserId(t.Teacher), t.North, subject, "item:retry-2"));
        Assert.Equal(WorkflowErrors.ActiveInstanceExists, active.Code);

        // The handler fails on the first delivery: the approval itself still succeeds and the event stays pending.
        factory.Handler.FailNextDelivery();
        await Ok(await Act(first.Id, "approve", t.Manager));
        Assert.Equal(1, factory.Handler.CallsFor(first.Id));
        Assert.Equal(0, factory.Handler.AppliedFor(first.Id));

        await using (var scope = factory.Services.CreateAsyncScope())
        using (scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginTenantScope(t.TenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<IWorkflowDbContext>();
            var msg = await db.Outbox.SingleAsync(m => m.InstanceId == first.Id);
            Assert.Equal((OutboxStatus.Pending, 1, "InvalidOperationException"), (msg.Status, msg.Attempts, msg.LastError));
            msg.NextAttemptAt = DateTime.UtcNow.AddMinutes(-1); // skip the backoff
            await db.SaveChangesAsync();
        }

        var dispatcher = factory.Services.GetRequiredService<IWorkflowOutboxDispatcher>();
        Assert.Equal(1, await dispatcher.DispatchAsync(first.Id));
        Assert.Equal(0, await dispatcher.DispatchAsync(first.Id));
        Assert.Equal(0, await dispatcher.DispatchAsync());
        Assert.Equal(1, factory.Handler.AppliedFor(first.Id));
        Assert.Equal(2, factory.Handler.CallsFor(first.Id));

        await using (var scope = factory.Services.CreateAsyncScope())
        using (scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginTenantScope(t.TenantId))
        {
            var msg = await scope.ServiceProvider.GetRequiredService<IWorkflowDbContext>().Outbox.SingleAsync(m => m.InstanceId == first.Id);
            Assert.Equal(OutboxStatus.Delivered, msg.Status);
            Assert.NotNull(msg.DeliveredAt);
        }
    }
}
