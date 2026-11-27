using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using EEMP.IntegrationTests.Infrastructure;
using EEMP.Modules.Finance.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using static EEMP.IntegrationTests.Infrastructure.Api;

namespace EEMP.IntegrationTests;

/// <summary>M3 finance: permissions, scope, guardian linkage, maker-checker, payments/receipts, online callbacks, void/credit, reports, PDFs.</summary>
[Collection(ApiCollection.Name)]
public class FinanceTests(EempFactory factory)
{
    private readonly HttpClient _c = factory.CreateClient();

    private static string Rv(JsonNode n) => n["rowVersion"]!.GetValue<string>();
    private static decimal Dec(JsonNode n, string k) => n[k]!.GetValue<decimal>();
    private static string Str(JsonNode n, string k) => n[k]!.GetValue<string>();

    private Task<string> Officer() => Demo(_c, "finance.officer").ContinueWith(t => t.Result.AccessToken, TaskScheduler.Default);

    private async Task<Guid> StudentAsync(string token, string number)
    {
        var items = (await Get(_c, $"/api/v1/students?search={number}&pageSize=100", token))["items"]!.AsArray();
        return Id(items.First(i => i!["studentNumber"]!.GetValue<string>() == number)!);
    }

    private async Task<(Guid North, Guid South)> BranchesAsync(string token)
    {
        var items = (await Get(_c, "/api/v1/branches", token))["items"]!.AsArray();
        return (Id(items.First(b => b!["code"]!.GetValue<string>() == "NORTH")!), Id(items.First(b => b!["code"]!.GetValue<string>() == "SOUTH")!));
    }

    /// <summary>A demo user holding the given role (system or custom) at a branch (null = tenant-wide).</summary>
    private async Task<string> UserWithRoleAsync(string userName, string roleCode, string[]? permissions, Guid? branchId)
    {
        var admin = (await Demo(_c, "demo.admin")).AccessToken;
        var roles = (await Get(_c, "/api/v1/roles", admin)).AsArray();
        var role = roles.FirstOrDefault(r => r!["code"]!.GetValue<string>() == roleCode)
            ?? await Ok(await Send(_c, HttpMethod.Post, "/api/v1/roles", admin, new { code = roleCode, name = roleCode, permissions }), HttpStatusCode.Created);
        var users = (await Get(_c, $"/api/v1/users?search={userName}", admin))["items"]!.AsArray();
        var user = users.FirstOrDefault(u => u!["userName"]!.GetValue<string>() == userName)
            ?? await Ok(await Send(_c, HttpMethod.Post, "/api/v1/users", admin, new { userName, displayName = userName, password = "Reader123" }), HttpStatusCode.Created);
        await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/users/{Id(user)}/assignments", admin, new[] { new { roleId = Id(role), branchId } }));
        return (await Login(_c, "demo", userName, "Reader123")).AccessToken;
    }

    private Task<string> Checker() => UserWithRoleAsync("fin.checker", "FinanceOfficer", null, null);

    private async Task<JsonNode> DraftAsync(string token, Guid studentId, decimal amount, string? due = null) =>
        await Ok(await Send(_c, HttpMethod.Post, "/api/v1/finance/invoices", token, new
        {
            studentId, dueDate = due, lines = new[] { new { description = "Books", quantity = (int?)1, unitPrice = (decimal?)amount } },
        }), HttpStatusCode.Created);

    private async Task<JsonNode> IssuedAsync(string token, Guid studentId, decimal amount, string? due = null)
    {
        var d = await DraftAsync(token, studentId, amount, due);
        return await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/finance/invoices/{Id(d)}/issue", token, new { rowVersion = Rv(d) }));
    }

    private async Task<JsonNode> PayAsync(string token, Guid studentId, decimal amount, Guid? invoiceId, string method = "cash", string? key = null) =>
        await Ok(await Send(_c, HttpMethod.Post, "/api/v1/finance/payments", token, new { studentId, method, amount, invoiceId, idempotencyKey = key }),
            HttpStatusCode.Created);

    [Fact]
    public async Task TenantAdmin_and_branch_manager_have_no_finance_access()
    {
        var admin = (await Demo(_c, "demo.admin")).AccessToken;
        var manager = (await Demo(_c, "branch.manager")).AccessToken;
        foreach (var t in new[] { admin, manager })
        {
            await Problem(await Send(_c, HttpMethod.Get, "/api/v1/finance/invoices", t), HttpStatusCode.Forbidden, "authorization.forbidden");
            await Problem(await Send(_c, HttpMethod.Get, "/api/v1/finance/payments", t), HttpStatusCode.Forbidden, "authorization.forbidden");
            await Problem(await Send(_c, HttpMethod.Get, "/api/v1/finance/reports/outstanding", t), HttpStatusCode.Forbidden, "authorization.forbidden");
            await Problem(await Send(_c, HttpMethod.Get, "/api/v1/finance/settings", t), HttpStatusCode.Forbidden, "authorization.forbidden");
        }
        var perms = (await Get(_c, "/api/v1/permissions", admin)).AsArray().Select(p => p!["code"]!.GetValue<string>()).ToList();
        Assert.Contains("finance.invoices.manage", perms);
        var roles = (await Get(_c, "/api/v1/roles", admin)).AsArray();
        var officer = roles.First(r => r!["code"]!.GetValue<string>() == "FinanceOfficer")!;
        Assert.True(officer["isSystem"]!.GetValue<bool>());
        Assert.DoesNotContain(roles.First(r => r!["code"]!.GetValue<string>() == "TenantAdmin")!["permissions"]!.AsArray(),
            p => p!.GetValue<string>().StartsWith("finance.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Seeded_demo_data_and_receipts()
    {
        var t = await Officer();
        var sara = await StudentAsync(t, "S0001");
        var account = await Get(_c, $"/api/v1/finance/students/{sara}/account", t);
        Assert.Equal("IRR", Str(account, "currency"));
        Assert.Contains(account["payments"]!.AsArray(), p => p!["receiptNumber"]?.GetValue<string>() == "R-000001");
        Assert.Contains(account["invoices"]!.AsArray(), i => i!["number"]?.GetValue<string>() == "INV-000001");
    }

    [Fact]
    public async Task Draft_issue_pay_overpay_credit_and_refund_with_maker_checker()
    {
        var maker = await Officer();
        var checker = await Checker();
        var sara = await StudentAsync(maker, "S0001");
        var draft = await DraftAsync(maker, sara, 1_000_000m);
        Assert.Equal("draft", Str(draft, "status"));
        Assert.Null(draft["number"]?.GetValue<string>());

        // Discount: maker requests, issuing is blocked while pending, maker cannot approve, checker approves.
        var disc = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/finance/invoices/{Id(draft)}/discounts", maker,
            new { kind = "scholarship", type = "percent", value = 10m, reason = "merit" }), HttpStatusCode.Created);
        draft = await Get(_c, $"/api/v1/finance/invoices/{Id(draft)}", maker);
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/finance/invoices/{Id(draft)}/issue", maker, new { rowVersion = Rv(draft) }),
            HttpStatusCode.UnprocessableEntity, "finance.discount_pending");
        Assert.Contains((await Get(_c, "/api/v1/finance/approvals", checker)).AsArray(), a => Str(a!, "id") == Str(disc, "id"));
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/finance/discounts/{Id(disc)}/approve", maker, new { rowVersion = Rv(disc) }),
            HttpStatusCode.Forbidden, "finance.maker_checker");
        var approved = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/finance/discounts/{Id(disc)}/approve", checker, new { rowVersion = Rv(disc) }));
        Assert.Equal("approved", Str(approved, "status"));
        Assert.Equal(100_000m, Dec(approved, "amount"));

        draft = await Get(_c, $"/api/v1/finance/invoices/{Id(draft)}", maker);
        Assert.Equal(900_000m, Dec(draft, "total"));
        var issued = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/finance/invoices/{Id(draft)}/issue", maker, new { rowVersion = Rv(draft) }));
        Assert.Matches("^INV-[0-9]{6}$", Str(issued, "number"));
        // Issued invoices cannot be edited.
        await Problem(await Send(_c, HttpMethod.Put, $"/api/v1/finance/invoices/{Id(issued)}", maker,
            new { rowVersion = Rv(issued), lines = new[] { new { description = "x", unitPrice = (decimal?)1m } } }), HttpStatusCode.Conflict, "finance.invalid_state");

        // Partial, then overpay: the remainder becomes credit on the payment.
        var p1 = await PayAsync(maker, sara, 400_000m, Id(issued), "pos");
        Assert.Matches("^R-[0-9]{6}$", Str(p1, "receiptNumber"));
        Assert.Equal("partiallyPaid", Str(await Get(_c, $"/api/v1/finance/invoices/{Id(issued)}", maker), "status"));
        var p2 = await PayAsync(maker, sara, 700_000m, Id(issued), "transfer");
        Assert.Equal(500_000m, Dec(p2, "allocated"));
        Assert.Equal(200_000m, Dec(p2, "unallocated"));
        var paid = await Get(_c, $"/api/v1/finance/invoices/{Id(issued)}", maker);
        Assert.Equal("paid", Str(paid, "status"));
        Assert.Equal(0m, Dec(paid, "balance"));

        // Refund only from credit, with approval by someone else.
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/finance/payments/{Id(p2)}/refunds", maker, new { amount = 250_000m, reason = "x" }),
            HttpStatusCode.UnprocessableEntity, "finance.refund_exceeds_credit");
        var refund = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/finance/payments/{Id(p2)}/refunds", maker, new { amount = 150_000m, reason = "overpaid" }),
            HttpStatusCode.Created);
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/finance/refunds/{Id(refund)}/approve", maker, new { rowVersion = Rv(refund) }),
            HttpStatusCode.Forbidden, "finance.maker_checker");
        var done = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/finance/refunds/{Id(refund)}/approve", checker, new { rowVersion = Rv(refund) }));
        Assert.Equal("approved", Str(done, "status"));
        // Replayed decision (e.g. an at-least-once workflow event) is idempotent.
        await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/finance/refunds/{Id(refund)}/approve", checker, new { rowVersion = Rv(refund) }));
        var after = await Get(_c, $"/api/v1/finance/payments/{Id(p2)}", maker);
        Assert.Equal(150_000m, Dec(after, "refunded"));
        Assert.Equal(50_000m, Dec(after, "unallocated"));

        // Receipt PDF.
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/finance/payments/{Id(p1)}/receipt/pdf?lang=fa");
        req.Headers.Authorization = new("Bearer", maker);
        var res = await _c.SendAsync(req);
        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        Assert.Matches("^receipt-R-[0-9]{6}\\.pdf$", res.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
    }

    [Fact]
    public async Task Manual_payment_is_idempotent_and_cheques_settle_on_clearing()
    {
        var t = await Officer();
        var sara = await StudentAsync(t, "S0001");
        var inv = await IssuedAsync(t, sara, 300_000m);
        var key = Guid.NewGuid().ToString("N");
        var a = await PayAsync(t, sara, 100_000m, Id(inv), key: key);
        var b = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/finance/payments", t, new { studentId = sara, method = "cash", amount = 100_000m, invoiceId = Id(inv), idempotencyKey = key }));
        Assert.Equal(Str(a, "id"), Str(b, "id"));
        Assert.Equal(100_000m, Dec(await Get(_c, $"/api/v1/finance/invoices/{Id(inv)}", t), "paidAmount"));

        var cheque = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/finance/payments", t, new
        {
            studentId = sara, method = "cheque", amount = 200_000m, invoiceId = Id(inv), cheque = new { number = "123456", bank = "Melli", dueDate = "2026-12-30" },
        }), HttpStatusCode.Created);
        Assert.Equal("pending", Str(cheque, "status"));
        Assert.Null(cheque["receiptNumber"]?.GetValue<string>());
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/finance/payments/{Id(cheque)}/receipt/pdf", t), HttpStatusCode.UnprocessableEntity, "finance.receipt_unavailable");
        var cleared = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/finance/payments/{Id(cheque)}/cheque-status", t, new { rowVersion = Rv(cheque), status = "cleared" }));
        Assert.Equal("succeeded", Str(cleared, "status"));
        Assert.Equal("paid", Str(await Get(_c, $"/api/v1/finance/invoices/{Id(inv)}", t), "status"));
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/finance/payments/{Id(cheque)}/cheque-status", t, new { rowVersion = Rv(cleared), status = "bounced" }),
            HttpStatusCode.Conflict, "finance.invalid_state");
    }

    [Fact]
    public async Task Concurrent_payments_get_distinct_gap_free_receipts_and_correct_balance()
    {
        var t = await Officer();
        var sara = await StudentAsync(t, "S0001");
        var inv = await IssuedAsync(t, sara, 1_000_000m);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            Send(_c, HttpMethod.Post, "/api/v1/finance/payments", t, new { studentId = sara, method = "cash", amount = 100_000m, invoiceId = Id(inv) })));
        var bodies = new List<JsonNode>();
        foreach (var r in results) bodies.Add(await Ok(r, HttpStatusCode.Created));
        var numbers = bodies.Select(b => int.Parse(Str(b, "receiptNumber")[2..], System.Globalization.CultureInfo.InvariantCulture)).Order().ToList();
        Assert.Equal(6, numbers.Distinct().Count());
        Assert.Equal(numbers[0] + 5, numbers[^1]); // consecutive: no gaps, no duplicates
        var after = await Get(_c, $"/api/v1/finance/invoices/{Id(inv)}", t);
        Assert.Equal(600_000m, Dec(after, "paidAmount"));
        Assert.Equal(400_000m, Dec(after, "balance"));
    }

    [Fact]
    public async Task Void_releases_allocations_and_credit_notes_are_bounded_by_the_balance()
    {
        var t = await Officer();
        var sara = await StudentAsync(t, "S0001");
        var inv = await IssuedAsync(t, sara, 500_000m);
        var pay = await PayAsync(t, sara, 200_000m, Id(inv));
        inv = await Get(_c, $"/api/v1/finance/invoices/{Id(inv)}", t);
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/finance/invoices/{Id(inv)}/credit-notes", t, new { rowVersion = Rv(inv), amount = 400_000m, reason = "x" }),
            HttpStatusCode.UnprocessableEntity, "finance.amount_exceeds_balance");
        inv = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/finance/invoices/{Id(inv)}/credit-notes", t, new { rowVersion = Rv(inv), amount = 100_000m, reason = "goodwill" }));
        Assert.Equal(200_000m, Dec(inv, "balance"));
        Assert.Matches("^CN-[0-9]{6}$", Str(inv["adjustments"]!.AsArray()[0]!, "number"));
        // Stale rowVersion is a conflict.
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/finance/invoices/{Id(inv)}/void", t, new { rowVersion = Rv(pay), reason = "x" }),
            HttpStatusCode.Conflict, "concurrency.conflict");
        var voided = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/finance/invoices/{Id(inv)}/void", t, new { rowVersion = Rv(inv), reason = "issued in error" }));
        Assert.Equal("void", Str(voided, "status"));
        Assert.Equal(0m, Dec(voided, "balance"));
        var p = await Get(_c, $"/api/v1/finance/payments/{Id(pay)}", t);
        Assert.Equal(0m, Dec(p, "allocated"));
        Assert.Equal(200_000m, Dec(p, "unallocated"));
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/finance/invoices/{Id(inv)}/void", t, new { rowVersion = Rv(voided), reason = "again" }),
            HttpStatusCode.Conflict, "finance.invalid_state");

        // The credit can be applied to another invoice.
        var next = await IssuedAsync(t, sara, 150_000m);
        var applied = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/finance/students/{sara}/apply-credit", t, new { invoiceId = Id(next) }));
        Assert.Equal("paid", Str(applied, "status"));
    }

    [Fact]
    public async Task Installments_and_late_fees()
    {
        var t = await Officer();
        var sara = await StudentAsync(t, "S0001");
        var draft = await DraftAsync(t, sara, 1_000_001m);
        var planned = await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/finance/invoices/{Id(draft)}/installments", t,
            new { rowVersion = Rv(draft), count = 3, firstDueDate = "2026-01-01", intervalDays = 30 }));
        Assert.Equal([333_333m, 333_333m, 333_335m], planned["installments"]!.AsArray().Select(i => Dec(i!, "amount")));
        await Problem(await Send(_c, HttpMethod.Put, $"/api/v1/finance/invoices/{Id(draft)}/installments", t,
            new { rowVersion = Rv(planned), items = new[] { new { dueDate = "2026-01-01", amount = (decimal?)10m } } }), HttpStatusCode.UnprocessableEntity, "finance.installments_mismatch");
        var issued = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/finance/invoices/{Id(planned)}/issue", t, new { rowVersion = Rv(planned) }));
        Assert.True(issued["isOverdue"]!.GetValue<bool>());
        Assert.Equal("overdue", Str(issued["installments"]!.AsArray()[0]!, "status"));

        var settings = await Get(_c, "/api/v1/finance/settings", t);
        var on = await Ok(await Send(_c, HttpMethod.Put, "/api/v1/finance/settings", t, new
        {
            vatEnabled = false, lateFeeEnabled = true, lateFeeType = "fixed", lateFeeValue = 50_000m, lateFeeGraceDays = 0,
            blockResultsOnOverdue = false, onlinePaymentsEnabled = true, rowVersion = Rv(settings),
        }));
        try
        {
            var first = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/finance/late-fees/apply", t, new { asOf = "2026-02-15" }));
            Assert.True(first["applied"]!.GetValue<int>() >= 2);
            var again = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/finance/late-fees/apply", t, new { asOf = "2026-02-15" }));
            Assert.Equal(0, again["applied"]!.GetValue<int>()); // idempotent
            var withFees = await Get(_c, $"/api/v1/finance/invoices/{Id(issued)}", t);
            Assert.Equal(100_000m, Dec(withFees, "lateFeeTotal"));
            Assert.Equal(1_100_001m, Dec(withFees, "balance"));
        }
        finally
        {
            await Ok(await Send(_c, HttpMethod.Put, "/api/v1/finance/settings", t, new
            {
                vatEnabled = false, lateFeeEnabled = false, lateFeeType = "fixed", lateFeeValue = 0m, blockResultsOnOverdue = false,
                onlinePaymentsEnabled = true, rowVersion = Rv(on),
            }));
        }
    }

    [Fact]
    public async Task Invoice_generation_from_enrollments_is_idempotent_including_concurrent_calls()
    {
        var t = await Officer();
        var type = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/finance/fee-types", t, new { code = $"G{Guid.NewGuid():N}"[..10], name = "Lab", taxable = false }), HttpStatusCode.Created);
        var fee = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/finance/fee-items", t, new { feeTypeId = Id(type), name = "Lab fee", amount = 250_000m }), HttpStatusCode.Created);
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/finance/fee-items", t, new { feeTypeId = Id(type), name = "Bad", amount = 10.5m }),
            HttpStatusCode.BadRequest, "validation.failed");
        var manager = (await Demo(_c, "branch.manager")).AccessToken;
        var mathA = (await Get(_c, "/api/v1/classes?search=MATH-7A", manager))["items"]!.AsArray().First(c => Str(c!, "code") == "MATH-7A")!;
        var sara = await StudentAsync(t, "S0001");
        var enrollment = Id((await Get(_c, $"/api/v1/enrollments?classId={Id(mathA)}&studentId={sara}", t))["items"]!.AsArray()[0]!);

        var runs = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            Send(_c, HttpMethod.Post, "/api/v1/finance/invoices/generate", t, new { enrollmentId = enrollment, feeItemIds = new[] { Id(fee) } })));
        var created = 0;
        foreach (var r in runs) created += (await Ok(r))["created"]!.GetValue<int>();
        Assert.Equal(1, created);
        var bulk = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/finance/invoices/generate-bulk", t, new { classId = Id(mathA), feeItemIds = new[] { Id(fee) } }));
        Assert.Equal(1, bulk["created"]!.GetValue<int>()); // Ali; Sara skipped
        Assert.Equal(1, bulk["skipped"]!.GetValue<int>());
        var gen = await Get(_c, $"/api/v1/finance/invoices/{bulk["invoiceIds"]!.AsArray()[0]!.GetValue<string>()}", t);
        Assert.Equal("draft", Str(gen, "status"));
        Assert.Equal(250_000m, Dec(gen, "total"));
        await Problem(await Send(_c, HttpMethod.Delete, $"/api/v1/finance/fee-items/{Id(fee)}", t), HttpStatusCode.Conflict, "finance.fee_item_in_use");
    }

    [Fact]
    public async Task Branch_scope_is_enforced()
    {
        var t = await Officer();
        var admin = (await Demo(_c, "demo.admin")).AccessToken;
        var (north, south) = await BranchesAsync(admin);
        var sara = await StudentAsync(t, "S0001"); // North
        var inv = await IssuedAsync(t, sara, 120_000m);
        var southOfficer = await UserWithRoleAsync("south.fin", "SouthFinance", ["finance.read", "finance.invoices.manage", "finance.payments.manage"], south);
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/finance/invoices/{Id(inv)}", southOfficer), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/finance/payments", southOfficer, new { studentId = sara, method = "cash", amount = 1_000m }),
            HttpStatusCode.NotFound, "resource.not_found");
        Assert.DoesNotContain((await Get(_c, "/api/v1/finance/invoices?pageSize=100", southOfficer))["items"]!.AsArray(), i => Str(i!, "id") == Str(inv, "id"));
        var northReader = await UserWithRoleAsync("north.finread", "NorthFinanceReader", ["finance.read"], north);
        await Ok(await Send(_c, HttpMethod.Get, $"/api/v1/finance/invoices/{Id(inv)}", northReader));
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/finance/invoices/{Id(inv)}/void", northReader, new { rowVersion = Rv(inv), reason = "x" }),
            HttpStatusCode.Forbidden, "authorization.forbidden");
    }

    [Fact]
    public async Task Portal_shows_only_own_or_linked_students_and_never_drafts()
    {
        var t = await Officer();
        var sara = await StudentAsync(t, "S0001");
        var ali = await StudentAsync(t, "S0002");
        var saraIssued = await IssuedAsync(t, sara, 90_000m);
        var saraDraft = await DraftAsync(t, sara, 80_000m);
        var aliIssued = await IssuedAsync(t, ali, 70_000m);
        var guardian = (await Demo(_c, "guardian.one")).AccessToken;
        var student = (await Demo(_c, "student.one")).AccessToken;
        var teacher = (await Demo(_c, "teacher.one")).AccessToken;

        foreach (var token in new[] { guardian, student })
        {
            var account = await Get(_c, $"/api/v1/portal/me/finance/students/{sara}/account", token);
            Assert.Contains(account["invoices"]!.AsArray(), i => Str(i!, "id") == Str(saraIssued, "id"));
            Assert.DoesNotContain(account["invoices"]!.AsArray(), i => Str(i!, "status") == "draft");
            await Ok(await Send(_c, HttpMethod.Get, $"/api/v1/portal/me/finance/invoices/{Id(saraIssued)}", token));
            await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/portal/me/finance/invoices/{Id(saraDraft)}", token), HttpStatusCode.NotFound, "resource.not_found");
            await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/portal/me/finance/invoices/{Id(aliIssued)}", token), HttpStatusCode.NotFound, "resource.not_found");
            await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/portal/me/finance/students/{ali}/account", token), HttpStatusCode.NotFound, "resource.not_found");
        }
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/portal/me/finance/students/{sara}/account", teacher), HttpStatusCode.NotFound, "resource.not_found");
        // Portal users cannot use the staff API.
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/finance/invoices/{Id(saraIssued)}", guardian), HttpStatusCode.Forbidden, "authorization.forbidden");

        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/portal/me/finance/invoices/{Id(saraIssued)}/pdf?lang=en");
        req.Headers.Authorization = new("Bearer", guardian);
        var pdf = await _c.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
    }

    private static string Authority(JsonNode started) => Str(started, "redirectUrl").Split('/')[^1];

    [Fact]
    public async Task Online_payment_callback_is_idempotent_and_verifies_the_stored_amount()
    {
        var t = await Officer();
        var sara = await StudentAsync(t, "S0001");
        var inv = await IssuedAsync(t, sara, 600_000m);
        var guardian = (await Demo(_c, "guardian.one")).AccessToken;

        // Client cannot inflate the amount.
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/portal/me/finance/invoices/{Id(inv)}/pay", guardian,
            new { amount = 700_000m, idempotencyKey = Guid.NewGuid().ToString("N") }), HttpStatusCode.UnprocessableEntity, "finance.amount_exceeds_balance");

        var key = Guid.NewGuid().ToString("N");
        var started = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/portal/me/finance/invoices/{Id(inv)}/pay", guardian, new { amount = 250_000m, idempotencyKey = key }));
        var again = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/portal/me/finance/invoices/{Id(inv)}/pay", guardian, new { amount = 250_000m, idempotencyKey = key }));
        Assert.Equal(Str(started, "paymentId"), Str(again, "paymentId"));
        Assert.Equal("fake", Str(started, "provider"));
        var paymentId = Str(started, "paymentId");
        var url = $"/api/v1/finance/gateway/fake/callback/{paymentId}?Authority={Authority(started)}&Status=OK";

        // Wrong authority or provider: nothing is revealed or changed.
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/finance/gateway/fake/callback/{paymentId}?Authority=FAKEWRONG&Status=OK", null),
            HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/finance/gateway/zarinpal/callback/{paymentId}?Authority={Authority(started)}&Status=OK", null),
            HttpStatusCode.NotFound, "resource.not_found");

        // Concurrent + replayed callbacks settle exactly once.
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Send(_c, HttpMethod.Get, url, null)));
        var statuses = new List<JsonNode>();
        foreach (var r in results) statuses.Add(await Ok(r));
        Assert.All(statuses, s => Assert.Equal("succeeded", Str(s, "status")));
        Assert.Single(statuses.Select(s => Str(s, "receiptNumber")).Distinct());
        var replay = await Ok(await Send(_c, HttpMethod.Get, url, null));
        Assert.Equal(Str(statuses[0], "receiptNumber"), Str(replay, "receiptNumber"));
        var after = await Get(_c, $"/api/v1/finance/invoices/{Id(inv)}", t);
        Assert.Equal(250_000m, Dec(after, "paidAmount"));
        var portalPayment = await Get(_c, $"/api/v1/portal/me/finance/payments/{paymentId}", guardian);
        Assert.Equal("online", Str(portalPayment, "method"));
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/portal/me/finance/payments/{paymentId}/receipt/pdf"))
        {
            req.Headers.Authorization = new("Bearer", guardian);
            Assert.Equal(HttpStatusCode.OK, (await _c.SendAsync(req)).StatusCode);
        }
        var teacher = (await Demo(_c, "teacher.one")).AccessToken;
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/portal/me/finance/payments/{paymentId}", teacher), HttpStatusCode.NotFound, "resource.not_found");
    }

    [Fact]
    public async Task Provider_reported_amount_tampering_fails_the_payment()
    {
        var t = await Officer();
        var sara = await StudentAsync(t, "S0001");
        var inv = await IssuedAsync(t, sara, 300_000m);
        var guardian = (await Demo(_c, "guardian.one")).AccessToken;
        var started = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/portal/me/finance/invoices/{Id(inv)}/pay", guardian, new { idempotencyKey = Guid.NewGuid().ToString("N") }));
        factory.Services.GetRequiredService<FakePaymentGateway>().Tamper(Authority(started), 1_000m);
        var res = await Ok(await Send(_c, HttpMethod.Get, $"/api/v1/finance/gateway/fake/callback/{Str(started, "paymentId")}?Authority={Authority(started)}&Status=OK", null));
        Assert.Equal("failed", Str(res, "status"));
        Assert.Equal("finance.amount_mismatch", Str(res, "failureCode"));
        Assert.Null(res["receiptNumber"]?.GetValue<string>());
        Assert.Equal(0m, Dec(await Get(_c, $"/api/v1/finance/invoices/{Id(inv)}", t), "paidAmount"));

        // Payer cancelled at the provider.
        var cancelled = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/portal/me/finance/invoices/{Id(inv)}/pay", guardian, new { idempotencyKey = Guid.NewGuid().ToString("N") }));
        var c = await Ok(await Send(_c, HttpMethod.Get, $"/api/v1/finance/gateway/fake/callback/{Str(cancelled, "paymentId")}?Authority={Authority(cancelled)}&Status=NOK", null));
        Assert.Equal("cancelled", Str(c, "status"));
    }

    [Fact]
    public async Task Reports_with_csv_export()
    {
        var t = await Officer();
        var sara = await StudentAsync(t, "S0001");
        var inv = await IssuedAsync(t, sara, 222_000m, "2026-01-10");
        await PayAsync(t, sara, 22_000m, Id(inv));
        var outstanding = (await Get(_c, "/api/v1/finance/reports/outstanding", t)).AsArray();
        var row = outstanding.First(r => Str(r!, "invoiceId") == Str(inv, "id"))!;
        Assert.Equal(200_000m, Dec(row, "balance"));
        var aging = await Get(_c, "/api/v1/finance/reports/aging?asOf=2026-03-01", t);
        Assert.True(Dec(aging["buckets"]!, "d31_60") >= 200_000m);
        var collections = (await Get(_c, "/api/v1/finance/reports/collections?from=2026-01-01&to=2026-12-31", t)).AsArray();
        Assert.Contains(collections, c => Str(c!, "method") == "cash" && Dec(c!, "amount") > 0);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/finance/reports/outstanding?format=csv");
        req.Headers.Authorization = new("Bearer", t);
        var res = await _c.SendAsync(req);
        Assert.Equal("text/csv", res.Content.Headers.ContentType?.MediaType);
        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.StartsWith("invoiceNumber,studentNumber", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), StringComparison.Ordinal);

        // Invoice PDF: drafts have none.
        var draft = await DraftAsync(t, sara, 1_000m);
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/finance/invoices/{Id(draft)}/pdf", t), HttpStatusCode.UnprocessableEntity, "finance.invoice_not_issued");
        using var pdfReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/finance/invoices/{Id(inv)}/pdf?lang=ar");
        pdfReq.Headers.Authorization = new("Bearer", t);
        var pdf = await _c.SendAsync(pdfReq);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Matches("^invoice-INV-[0-9]{6}\\.pdf$", pdf.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
    }

    [Fact]
    public async Task Overdue_balance_blocks_portal_results_only_when_enabled()
    {
        var t = await Officer();
        var sara = await StudentAsync(t, "S0001");
        await IssuedAsync(t, sara, 10_000m, "2026-01-01"); // overdue
        var guardian = (await Demo(_c, "guardian.one")).AccessToken;
        await Ok(await Send(_c, HttpMethod.Get, $"/api/v1/portal/me/students/{sara}/grades", guardian));
        var s = await Get(_c, "/api/v1/finance/settings", t);
        var on = await Ok(await Send(_c, HttpMethod.Put, "/api/v1/finance/settings", t, new
        {
            vatEnabled = false, lateFeeEnabled = false, blockResultsOnOverdue = true, onlinePaymentsEnabled = true, rowVersion = Rv(s),
        }));
        try
        {
            await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/portal/me/students/{sara}/grades", guardian), HttpStatusCode.Forbidden, "finance.results_blocked");
            await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/portal/me/students/{sara}/report-cards", guardian), HttpStatusCode.Forbidden, "finance.results_blocked");
            // Staff views are not affected.
            var manager = (await Demo(_c, "branch.manager")).AccessToken;
            await Ok(await Send(_c, HttpMethod.Get, $"/api/v1/report-cards?studentId={sara}", manager));
        }
        finally
        {
            await Ok(await Send(_c, HttpMethod.Put, "/api/v1/finance/settings", t, new
            {
                vatEnabled = false, lateFeeEnabled = false, blockResultsOnOverdue = false, onlinePaymentsEnabled = true, rowVersion = Rv(on),
            }));
        }
        await Ok(await Send(_c, HttpMethod.Get, $"/api/v1/portal/me/students/{sara}/grades", guardian));
    }

    [Fact]
    public async Task Finance_data_is_tenant_isolated()
    {
        var t = await Officer();
        var sara = await StudentAsync(t, "S0001");
        var inv = await IssuedAsync(t, sara, 55_000m);
        var pay = await PayAsync(t, sara, 5_000m, Id(inv));

        var platform = await Login(_c, null, "platform.admin", EempFactory.PlatformPassword);
        var slug = $"f{Guid.NewGuid():N}"[..12];
        await Ok(await Send(_c, HttpMethod.Post, "/api/v1/platform/tenants", platform.AccessToken, new
        {
            slug, name = "Other Finance Institute", adminUser = new { userName = "fo.admin", displayName = "Admin", password = "OtherAdmin1" },
        }), HttpStatusCode.Created);
        var otherAdmin = (await Login(_c, slug, "fo.admin", "OtherAdmin1")).AccessToken;
        var role = (await Get(_c, "/api/v1/roles", otherAdmin)).AsArray().First(r => Str(r!, "code") == "FinanceOfficer")!;
        var user = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/users", otherAdmin, new { userName = "fo.officer", displayName = "x", password = "OtherOfficer1" }), HttpStatusCode.Created);
        await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/users/{Id(user)}/assignments", otherAdmin, new[] { new { roleId = Id(role), branchId = (Guid?)null } }));
        var other = (await Login(_c, slug, "fo.officer", "OtherOfficer1")).AccessToken;

        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/finance/invoices/{Id(inv)}", other), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/finance/payments/{Id(pay)}", other), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/finance/students/{sara}/account", other), HttpStatusCode.NotFound, "resource.not_found");
        Assert.Equal(0, (await Get(_c, "/api/v1/finance/invoices", other))["totalCount"]!.GetValue<int>());
        Assert.Empty((await Get(_c, "/api/v1/finance/reports/outstanding", other)).AsArray());
        // Numbering is per tenant: the other tenant's first receipt is R-000001.
        Assert.Empty((await Get(_c, "/api/v1/finance/fee-types", other)).AsArray());
    }
}
