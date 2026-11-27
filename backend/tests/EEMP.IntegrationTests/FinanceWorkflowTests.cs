using System.Net;
using System.Text.Json.Nodes;
using EEMP.IntegrationTests.Infrastructure;
using EEMP.Modules.Workflow.Contracts;
using EEMP.Modules.Workflow.Infrastructure;
using EEMP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static EEMP.IntegrationTests.Infrastructure.Api;

namespace EEMP.IntegrationTests;

/// <summary>
/// DL-081..DL-085: Finance discount/refund approvals run on the EEMP-011 workflow engine (inbox, maker-checker, return/resubmit,
/// idempotent outcome delivery, finance.approve-only steps), and finance records block deleting students and branches.
/// </summary>
[Collection(ApiCollection.Name)]
public class FinanceWorkflowTests(EempFactory factory)
{
    private readonly HttpClient _c = factory.CreateClient();

    private static string Rv(JsonNode n) => n["rowVersion"]!.GetValue<string>();
    private static string Str(JsonNode n, string k) => n[k]!.GetValue<string>();
    private static decimal Dec(JsonNode n, string k) => n[k]!.GetValue<decimal>();
    private static Guid Wf(JsonNode n) => Guid.Parse(n["workflowInstanceId"]!.GetValue<string>());

    private async Task<string> Officer() => (await Demo(_c, "finance.officer")).AccessToken;

    /// <summary>A second FinanceOfficer (the checker), tenant-wide.</summary>
    private async Task<(string Token, Guid Id)> CheckerAsync()
    {
        const string userName = "fin.wf.checker";
        var admin = (await Demo(_c, "demo.admin")).AccessToken;
        var role = (await Get(_c, "/api/v1/roles", admin)).AsArray().First(r => r!["code"]!.GetValue<string>() == "FinanceOfficer")!;
        var users = (await Get(_c, $"/api/v1/users?search={userName}", admin))["items"]!.AsArray();
        var user = users.FirstOrDefault(u => u!["userName"]!.GetValue<string>() == userName)
            ?? await Ok(await Send(_c, HttpMethod.Post, "/api/v1/users", admin, new { userName, displayName = userName, password = "Checker123" }), HttpStatusCode.Created);
        await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/users/{Id(user)}/assignments", admin, new[] { new { roleId = Id(role), branchId = (Guid?)null } }));
        return ((await Login(_c, "demo", userName, "Checker123")).AccessToken, Id(user));
    }

    private async Task<Guid> StudentAsync(string token, string number)
    {
        var items = (await Get(_c, $"/api/v1/students?search={number}&pageSize=100", token))["items"]!.AsArray();
        return Id(items.First(i => i!["studentNumber"]!.GetValue<string>() == number)!);
    }

    private async Task<JsonNode> DraftAsync(string token, Guid studentId, decimal amount) =>
        await Ok(await Send(_c, HttpMethod.Post, "/api/v1/finance/invoices", token, new
        {
            studentId, lines = new[] { new { description = "Workshop", quantity = (int?)1, unitPrice = (decimal?)amount } },
        }), HttpStatusCode.Created);

    private async Task<List<Guid>> InboxAsync(string token, string key) =>
        (await Get(_c, $"/api/v1/workflow/inbox?definitionKey={key}&pageSize=100", token))["items"]!.AsArray().Select(i => Id(i!)).ToList();

    private async Task<JsonNode> ActAsync(string token, Guid instanceId, string action, string? comment)
    {
        var detail = await Get(_c, $"/api/v1/workflow/instances/{instanceId}", token);
        return await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/workflow/instances/{instanceId}/{action}", token, new { rowVersion = Rv(detail), comment }));
    }

    private async Task<JsonNode> DiscountAsync(string token, Guid invoiceId, Guid discountId) =>
        (await Get(_c, $"/api/v1/finance/invoices/{invoiceId}", token))["discounts"]!.AsArray().First(d => Id(d!) == discountId)!;

    [Fact]
    public async Task Discount_is_approved_from_the_workflow_inbox_by_another_finance_officer_and_applied_once()
    {
        var maker = await Officer();
        var (checker, checkerId) = await CheckerAsync();
        var draft = await DraftAsync(maker, await StudentAsync(maker, "S0002"), 2_000_000m);
        var disc = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/finance/invoices/{Id(draft)}/discounts", maker,
            new { kind = "scholarship", type = "percent", value = 25m, reason = "merit" }), HttpStatusCode.Created);
        Assert.Equal("pending", Str(disc, "status"));
        var instanceId = Wf(disc);

        // The engine routes it to finance.approve holders other than the maker (DL-083 default definition, provisioned on first use).
        var instance = await Get(_c, $"/api/v1/workflow/instances/{instanceId}", checker);
        Assert.Equal("discount-approval", Str(instance, "definitionKey"));
        Assert.Equal("finance.approve", Str(instance["currentStep"]!, "permission"));
        Assert.Contains(instanceId, await InboxAsync(checker, "discount-approval"));
        Assert.DoesNotContain(instanceId, await InboxAsync(maker, "discount-approval"));
        Assert.DoesNotContain(instanceId, await InboxAsync((await Demo(_c, "branch.manager")).AccessToken, "discount-approval"));
        var mine = await Get(_c, $"/api/v1/workflow/instances/{instanceId}", maker);
        Assert.DoesNotContain("approve", mine["allowedActions"]!.AsArray().Select(a => a!.GetValue<string>()));
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/workflow/instances/{instanceId}/approve", maker, new { rowVersion = Rv(mine) }),
            HttpStatusCode.Forbidden, "workflow.self_approval_forbidden");
        // The /finance wrapper keeps its maker-checker code.
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/finance/discounts/{Id(disc)}/approve", maker, new { rowVersion = Rv(disc) }),
            HttpStatusCode.Forbidden, "finance.maker_checker");

        await ActAsync(checker, instanceId, "approve", "Merit confirmed");
        var applied = await DiscountAsync(maker, Id(draft), Id(disc));
        Assert.Equal("approved", Str(applied, "status"));
        Assert.Equal(500_000m, Dec(applied, "amount"));
        Assert.Equal("Merit confirmed", Str(applied, "decisionNote"));
        Assert.Equal(checkerId, Guid.Parse(Str(applied, "decidedBy")));
        Assert.Equal(1_500_000m, Dec(await Get(_c, $"/api/v1/finance/invoices/{Id(draft)}", maker), "total"));

        // At-least-once delivery: replaying the completion event changes nothing.
        await ReplayAsync(instanceId, "discount", Id(disc), WorkflowEventTypes.Completed, WorkflowOutcomes.Approved);
        await ReplayAsync(instanceId, "discount", Id(disc), WorkflowEventTypes.Completed, WorkflowOutcomes.Rejected);
        var again = await DiscountAsync(maker, Id(draft), Id(disc));
        Assert.Equal("approved", Str(again, "status"));
        Assert.Equal(1_500_000m, Dec(await Get(_c, $"/api/v1/finance/invoices/{Id(draft)}", maker), "total"));
        // An event for an instance Finance did not record is ignored.
        await ReplayAsync(Guid.NewGuid(), "discount", Id(disc), WorkflowEventTypes.Completed, WorkflowOutcomes.Rejected);
        Assert.Equal("approved", Str(await DiscountAsync(maker, Id(draft), Id(disc)), "status"));

        var issued = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/finance/invoices/{Id(draft)}/issue", maker,
            new { rowVersion = Rv(await Get(_c, $"/api/v1/finance/invoices/{Id(draft)}", maker)) }));
        Assert.Matches("^INV-[0-9]{6}$", Str(issued, "number"));
    }

    [Fact]
    public async Task Returned_discount_is_edited_and_resubmitted_then_rejected_through_the_finance_wrapper()
    {
        var maker = await Officer();
        var (checker, _) = await CheckerAsync();
        var draft = await DraftAsync(maker, await StudentAsync(maker, "S0002"), 1_000_000m);
        var disc = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/finance/invoices/{Id(draft)}/discounts", maker,
            new { kind = "discount", type = "fixed", value = 100_000m, reason = "first try" }), HttpStatusCode.Created);
        var instanceId = Wf(disc);

        await ActAsync(checker, instanceId, "return", "Attach the reason in detail");
        var returned = await DiscountAsync(maker, Id(draft), Id(disc));
        Assert.Equal("returned", Str(returned, "status"));
        draft = await Get(_c, $"/api/v1/finance/invoices/{Id(draft)}", maker);
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/finance/invoices/{Id(draft)}/issue", maker, new { rowVersion = Rv(draft) }),
            HttpStatusCode.UnprocessableEntity, "finance.discount_pending");
        // Only the maker edits; the approver cannot.
        await Problem(await Send(_c, HttpMethod.Put, $"/api/v1/finance/discounts/{Id(disc)}", checker,
            new { kind = "discount", type = "fixed", value = 50_000m, reason = "x", rowVersion = Rv(returned) }), HttpStatusCode.Forbidden, "authorization.forbidden");
        var resubmitted = await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/finance/discounts/{Id(disc)}", maker,
            new { kind = "discount", type = "fixed", value = 80_000m, reason = "Sibling of S0001, see file", rowVersion = Rv(returned) }));
        Assert.Equal("pending", Str(resubmitted, "status"));
        Assert.Equal(80_000m, Dec(resubmitted, "value"));
        var instance = await Get(_c, $"/api/v1/workflow/instances/{instanceId}", checker);
        Assert.Equal("pending", Str(instance, "status"));
        Assert.Equal(2, instance["round"]!.GetValue<int>());

        // The finance wrapper requires a note to reject (the engine requires a comment).
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/finance/discounts/{Id(disc)}/reject", checker, new { rowVersion = Rv(resubmitted) }),
            HttpStatusCode.BadRequest, "validation.failed");
        var rejected = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/finance/discounts/{Id(disc)}/reject", checker,
            new { rowVersion = Rv(resubmitted), note = "Not eligible" }));
        Assert.Equal("rejected", Str(rejected, "status"));
        Assert.Equal("Not eligible", Str(rejected, "decisionNote"));
        Assert.Equal("rejected", Str(await Get(_c, $"/api/v1/workflow/instances/{instanceId}", checker), "status"));
        Assert.Equal(1_000_000m, Dec(await Get(_c, $"/api/v1/finance/invoices/{Id(draft)}", maker), "total"));
    }

    [Fact]
    public async Task Withdrawing_a_discount_in_finance_cancels_its_workflow_instance()
    {
        var maker = await Officer();
        var (checker, _) = await CheckerAsync();
        var draft = await DraftAsync(maker, await StudentAsync(maker, "S0002"), 600_000m);
        var disc = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/finance/invoices/{Id(draft)}/discounts", maker,
            new { kind = "discount", type = "percent", value = 5m, reason = "withdraw me" }), HttpStatusCode.Created);
        var res = await Send(_c, HttpMethod.Delete, $"/api/v1/finance/discounts/{Id(disc)}", maker);
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.Equal("cancelled", Str(await Get(_c, $"/api/v1/workflow/instances/{Wf(disc)}", maker), "status"));
        Assert.DoesNotContain(Wf(disc), await InboxAsync(checker, "discount-approval"));
    }

    [Fact]
    public async Task Refund_is_approved_from_the_inbox_and_a_cancelled_request_is_rejected()
    {
        var maker = await Officer();
        var (checker, _) = await CheckerAsync();
        // Credit: a payment for a new student with no invoices stays unallocated.
        var admin = (await Demo(_c, "demo.admin")).AccessToken;
        var north = Id((await Get(_c, "/api/v1/branches", admin))["items"]!.AsArray().First(b => Str(b!, "code") == "NORTH")!);
        var student = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/students", (await Demo(_c, "branch.manager")).AccessToken,
            new { branchId = north, firstName = "Credit", lastName = $"Holder{Random.Shared.Next(1000, 9999)}" }), HttpStatusCode.Created);
        var pay = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/finance/payments", maker,
            new { studentId = Id(student), method = "cash", amount = 300_000m, invoiceId = (Guid?)null, idempotencyKey = Guid.NewGuid().ToString("N") }), HttpStatusCode.Created);
        var credit = Dec(pay, "unallocated");
        Assert.Equal(300_000m, credit);

        var refund = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/finance/payments/{Id(pay)}/refunds", maker, new { amount = 100_000m, reason = "overpaid" }),
            HttpStatusCode.Created);
        var instance = await Get(_c, $"/api/v1/workflow/instances/{Wf(refund)}", checker);
        Assert.Equal("refund-approval", Str(instance, "definitionKey"));
        await ActAsync(checker, Wf(refund), "approve", null);
        var after = await Get(_c, $"/api/v1/finance/payments/{Id(pay)}", maker);
        Assert.Equal(100_000m, Dec(after, "refunded"));
        // Replay: no double refund.
        await ReplayAsync(Wf(refund), "refund", Id(refund), WorkflowEventTypes.Completed, WorkflowOutcomes.Approved);
        Assert.Equal(100_000m, Dec(await Get(_c, $"/api/v1/finance/payments/{Id(pay)}", maker), "refunded"));

        // The maker cancels a second request from the workflow side: Finance marks it rejected and releases the reserved credit.
        var second = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/finance/payments/{Id(pay)}/refunds", maker, new { amount = credit - 100_000m, reason = "rest" }),
            HttpStatusCode.Created);
        await ActAsync(maker, Wf(second), "cancel", "changed my mind");
        var refunds = (await Get(_c, "/api/v1/finance/refunds?status=rejected", maker)).AsArray();
        Assert.Contains(refunds, r => Id(r!) == Id(second));
        Assert.Equal(100_000m, Dec(await Get(_c, $"/api/v1/finance/payments/{Id(pay)}", maker), "refunded"));
        await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/finance/payments/{Id(pay)}/refunds", maker, new { amount = credit - 100_000m, reason = "rest again" }),
            HttpStatusCode.Created);
    }

    [Fact]
    public async Task Finance_approval_steps_can_only_require_finance_approve()
    {
        var maker = await Officer();
        // Make sure refund-approval exists (provisioned on first use), then try to publish a version approved by another permission.
        var admin = (await Demo(_c, "demo.admin")).AccessToken;
        var defs = (await Get(_c, "/api/v1/workflow/definitions", admin)).AsArray();
        var def = defs.First(d => Str(d!, "key") == "discount-approval")!;
        var draft = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/workflow/definitions/{Id(def)}/versions", admin), HttpStatusCode.Created);
        var versionNo = draft["versions"]!.AsArray().Max(v => v!["versionNo"]!.GetValue<int>());
        var version = draft["versions"]!.AsArray().First(v => v!["versionNo"]!.GetValue<int>() == versionNo)!;
        await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/workflow/definitions/{Id(def)}/versions/{versionNo}", admin,
            new { rowVersion = Rv(version), steps = new[] { new { name = "Anyone", permission = "workflow.tasks.approve" } } }));
        draft = await Get(_c, $"/api/v1/workflow/definitions/{Id(def)}", admin);
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/workflow/definitions/{Id(def)}/versions/{versionNo}/publish", admin, new { rowVersion = Rv(draft) }),
            HttpStatusCode.UnprocessableEntity, "workflow.step_permission_not_allowed");
        // Put the draft back to finance.approve so later tests/publishes are unaffected.
        version = draft["versions"]!.AsArray().First(v => v!["versionNo"]!.GetValue<int>() == versionNo)!;
        await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/workflow/definitions/{Id(def)}/versions/{versionNo}", admin,
            new { rowVersion = Rv(version), steps = new[] { new { name = "Finance approval", permission = "finance.approve", slaHours = 48 } } }));
        Assert.NotNull(maker);
    }

    [Fact]
    public async Task Students_and_branches_with_finance_records_cannot_be_deleted()
    {
        var admin = (await Demo(_c, "demo.admin")).AccessToken;
        var officer = await Officer();
        var branch = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/branches", admin,
            new { code = $"F{Random.Shared.Next(1000, 9999)}", name = "Finance Branch" }), HttpStatusCode.Created);
        var manager = (await Demo(_c, "branch.manager")).AccessToken;
        var north = Id((await Get(_c, "/api/v1/branches", admin))["items"]!.AsArray().First(b => Str(b!, "code") == "NORTH")!);
        var student = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/students", manager,
            new { branchId = north, firstName = "Fin", lastName = $"Del{Random.Shared.Next(1000, 9999)}" }), HttpStatusCode.Created);
        await DraftAsync(officer, Id(student), 100_000m);
        await Problem(await Send(_c, HttpMethod.Delete, $"/api/v1/students/{Id(student)}", manager), HttpStatusCode.Conflict, "finance.records_exist");

        var type = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/finance/fee-types", officer,
            new { code = $"D{Guid.NewGuid():N}"[..10], name = "Del", taxable = false }), HttpStatusCode.Created);
        await Ok(await Send(_c, HttpMethod.Post, "/api/v1/finance/fee-items", officer,
            new { feeTypeId = Id(type), name = "Branch fee", amount = 10_000m, branchId = Id(branch) }), HttpStatusCode.Created);
        await Problem(await Send(_c, HttpMethod.Delete, $"/api/v1/branches/{Id(branch)}", admin), HttpStatusCode.Conflict, "finance.records_exist");

        // Without finance records the delete still works.
        var free = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/students", manager,
            new { branchId = north, firstName = "Free", lastName = $"Del{Random.Shared.Next(1000, 9999)}" }), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(_c, HttpMethod.Delete, $"/api/v1/students/{Id(free)}", manager)).StatusCode);
    }

    /// <summary>Redelivers a workflow event to Finance's handler, as the outbox would after a crash between apply and acknowledgement.</summary>
    private async Task ReplayAsync(Guid instanceId, string subjectType, Guid subjectId, string eventType, string outcome)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var wf = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var tenantId = await wf.Instances.IgnoreQueryFilters().Where(i => i.SubjectId == subjectId).Select(i => i.TenantId).FirstAsync();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var _ = tenant.BeginTenantScope(tenantId);
        var handler = scope.ServiceProvider.GetServices<IWorkflowEventHandler>().Single(h => h.SubjectModule == "finance");
        await handler.HandleAsync(new WorkflowEvent(Guid.NewGuid(), tenantId, instanceId, $"{subjectType}-approval", 1, "finance", subjectType, subjectId,
            null, eventType, outcome, null, Guid.Empty, DateTime.UtcNow), CancellationToken.None);
    }
}
