using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using EEMP.IntegrationTests.Infrastructure;
using static EEMP.IntegrationTests.Infrastructure.Api;

namespace EEMP.IntegrationTests;

/// <summary>Report-card PDF export: admin (branch scope), portal (published + relationship only), archive, tenant isolation.</summary>
[Collection(ApiCollection.Name)]
public class ReportCardPdfTests(EempFactory factory)
{
    private readonly HttpClient _c = factory.CreateClient();

    private static string Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran")).DateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..(prefix.Length + 8)].ToUpperInvariant();

    private static string Rv(JsonNode n) => n["rowVersion"]!.GetValue<string>();

    private async Task<HttpResponseMessage> Download(string url, string? token, string? acceptLanguage = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (acceptLanguage is not null) req.Headers.AcceptLanguage.ParseAdd(acceptLanguage);
        return await _c.SendAsync(req);
    }

    private static async Task<byte[]> Pdf(HttpResponseMessage res)
    {
        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.True(res.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)res.StatusCode}: {Encoding.UTF8.GetString(bytes)}");
        Assert.Equal("application/pdf", res.Content.Headers.ContentType?.MediaType);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        var disposition = res.Content.Headers.ContentDisposition;
        Assert.Equal("attachment", disposition?.DispositionType);
        Assert.Matches("^report-card-[0-9a-f]{8}-v[0-9]+\\.pdf$", disposition!.FileName!.Trim('"'));
        Assert.Contains("no-store", res.Headers.CacheControl?.ToString() ?? "");
        return bytes;
    }

    private sealed record Setup(string Manager, Guid ClassId, Guid SaraCard, Guid AliCard);

    /// <summary>A fresh term/class in North with Sara (S0001, linked to guardian.one / student.one) and Ali (S0002), one approved assessment, generated drafts.</summary>
    private async Task<Setup> SetupAsync()
    {
        var admin = (await Demo(_c, "demo.admin")).AccessToken;
        var manager = (await Demo(_c, "branch.manager")).AccessToken;
        var north = Id((await Get(_c, "/api/v1/branches", admin))["items"]!.AsArray().First(b => b!["code"]!.GetValue<string>() == "NORTH")!);
        var year = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/academic-years", admin,
            new { name = Unique("Y-"), startDate = Shift(Today, -60), endDate = Shift(Today, 300), status = "active" }), HttpStatusCode.Created);
        var term = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/terms", admin,
            new { academicYearId = Id(year), name = "T1", startDate = Shift(Today, -30), endDate = Shift(Today, 120) }), HttpStatusCode.Created);
        var subject = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/subjects", admin, new { code = Unique("SB"), name = "Geometry" }), HttpStatusCode.Created);
        var course = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/courses", admin,
            new { subjectId = Id(subject), code = Unique("CO"), name = "Geometry 8", gradeLevel = "8" }), HttpStatusCode.Created);
        var cls = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/classes", manager,
            new { termId = Id(term), courseId = Id(course), branchId = north, code = Unique("K"), name = "Class", capacity = 30, status = "active" }), HttpStatusCode.Created);
        async Task<Guid> Student(string number)
        {
            var items = (await Get(_c, $"/api/v1/students?search={number}&pageSize=100", manager))["items"]!.AsArray();
            return Id(items.First(i => i!["studentNumber"]!.GetValue<string>() == number)!);
        }
        var sara = await Student("S0001");
        var ali = await Student("S0002");
        foreach (var s in new[] { sara, ali })
            await Ok(await Send(_c, HttpMethod.Post, "/api/v1/enrollments", manager, new { classId = Id(cls), studentId = s }), HttpStatusCode.Created);
        var a = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/classes/{Id(cls)}/assessments", manager,
            new { name = "Final", type = "final", weight = 1, maxScore = 20 }), HttpStatusCode.Created);
        var r = await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/assessments/{Id(a)}/results", manager, new
        {
            rowVersion = Rv(a),
            results = new[] { new { studentId = sara, mark = "scored", score = (decimal?)18.5m }, new { studentId = ali, mark = "absent", score = (decimal?)null } },
        }));
        a = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/assessments/{Id(a)}/submit", manager, new { rowVersion = Rv(r["assessment"]!) }));
        await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/assessments/{Id(a)}/approve", manager, new { rowVersion = Rv(a) }));
        var gen = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/report-cards/generate", manager, new { termId = Id(term), classId = Id(cls) }));
        var items = gen["items"]!.AsArray();
        Guid Card(Guid student) => Id(items.First(i => Guid.Parse(i!["studentId"]!.GetValue<string>()) == student)!);
        return new Setup(manager, Id(cls), Card(sara), Card(ali));
    }

    [Fact]
    public async Task Admin_downloads_any_version_in_scope_with_language_selection()
    {
        var x = await SetupAsync();
        var url = $"/api/v1/report-cards/{x.SaraCard}/versions/1/pdf";

        // Draft versions are downloadable by staff with reportcards.read; language from ?lang or Accept-Language.
        var en = await Pdf(await Download($"{url}?lang=en", x.Manager));
        var fa = await Pdf(await Download(url, x.Manager, "fa-IR"));
        await Pdf(await Download($"{url}?lang=ar", x.Manager));
        await Pdf(await Download($"{url}?lang=tr", x.Manager));
        Assert.NotEqual(en.Length, fa.Length);
        var disposition = (await Download(url, x.Manager)).Content.Headers.ContentDisposition!.ToString();
        Assert.DoesNotContain("Sara", disposition, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("S0001", disposition, StringComparison.Ordinal);

        var bad = await Download($"{url}?lang=de", x.Manager);
        await Problem(bad, HttpStatusCode.BadRequest, "validation.failed");
        Assert.Contains("validation.unsupported_locale", await bad.Content.ReadAsStringAsync());
        await Problem(await Download($"/api/v1/report-cards/{x.SaraCard}/versions/7/pdf", x.Manager), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Download($"/api/v1/report-cards/{Guid.NewGuid()}/versions/1/pdf", x.Manager), HttpStatusCode.NotFound, "resource.not_found");

        // No token → 401; tenant admin and teacher lack reportcards.read → 403 (sensitive data, not implicit for admins).
        await Problem(await Download(url, null), HttpStatusCode.Unauthorized, "auth.unauthorized");
        await Problem(await Download(url, (await Demo(_c, "demo.admin")).AccessToken), HttpStatusCode.Forbidden, "authorization.forbidden");
        await Problem(await Download(url, (await Demo(_c, "teacher.one")).AccessToken), HttpStatusCode.Forbidden, "authorization.forbidden");

        // Branch scope: a South-only report-card reader cannot see the North card (404, no existence leak).
        var south = await RoleUserAsync("south.pdf", "SouthPdf", ["reportcards.read"], "SOUTH");
        await Problem(await Download(url, south), HttpStatusCode.NotFound, "resource.not_found");
    }

    [Fact]
    public async Task Portal_downloads_only_published_cards_of_own_or_linked_students()
    {
        var x = await SetupAsync();
        var guardian = (await Demo(_c, "guardian.one")).AccessToken;
        var student = (await Demo(_c, "student.one")).AccessToken;
        var teacher = (await Demo(_c, "teacher.one")).AccessToken;
        var sara = $"/api/v1/portal/me/report-cards/{x.SaraCard}/pdf";
        var ali = $"/api/v1/portal/me/report-cards/{x.AliCard}/pdf";

        // Unpublished → 404 for guardian and student.
        await Problem(await Download(sara, guardian), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Download(sara, student), HttpStatusCode.NotFound, "resource.not_found");

        var published = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/report-cards/publish", x.Manager,
            new { termId = (await Get(_c, $"/api/v1/report-cards/{x.SaraCard}", x.Manager))["termId"]!.GetValue<string>(), classId = x.ClassId }));
        Assert.Equal(2, published["published"]!.GetValue<int>());

        await Pdf(await Download($"{sara}?lang=fa-IR", guardian));
        await Pdf(await Download(sara, student, "en"));
        // Guardian is not linked to Ali (S0002); the student is not Ali; a teacher is not a portal reader of report cards.
        await Problem(await Download(ali, guardian), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Download(ali, student), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Download(sara, teacher), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Download($"/api/v1/portal/me/report-cards/{Guid.NewGuid()}/pdf", guardian), HttpStatusCode.NotFound, "resource.not_found");

        // Archive of the class's published cards (admin).
        var zipRes = await Download($"/api/v1/report-cards/pdf-archive?classId={x.ClassId}&lang=en", x.Manager);
        var zipBytes = await zipRes.Content.ReadAsByteArrayAsync();
        Assert.True(zipRes.StatusCode == HttpStatusCode.OK, Encoding.UTF8.GetString(zipBytes));
        Assert.Equal("application/zip", zipRes.Content.Headers.ContentType?.MediaType);
        using var zip = new ZipArchive(new MemoryStream(zipBytes));
        Assert.Equal(["S0001-v1.pdf", "S0002-v1.pdf"], zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ToArray());
        foreach (var entry in zip.Entries)
        {
            using var s = entry.Open();
            var head = new byte[5];
            s.ReadExactly(head);
            Assert.Equal("%PDF-", Encoding.ASCII.GetString(head));
        }
        await Problem(await Download("/api/v1/report-cards/pdf-archive", x.Manager), HttpStatusCode.BadRequest, "validation.failed");
        await Problem(await Download($"/api/v1/report-cards/pdf-archive?classId={x.ClassId}", guardian), HttpStatusCode.Forbidden, "authorization.forbidden");
        var south = await RoleUserAsync("south.pdf", "SouthPdf", ["reportcards.read"], "SOUTH");
        await Problem(await Download($"/api/v1/report-cards/pdf-archive?classId={x.ClassId}", south), HttpStatusCode.NotFound, "resource.not_found");
    }

    [Fact]
    public async Task Report_card_pdfs_are_isolated_between_tenants()
    {
        var x = await SetupAsync();
        var platform = await Login(_c, null, "platform.admin", EempFactory.PlatformPassword);
        var slug = $"p{Guid.NewGuid():N}"[..12];
        await Ok(await Send(_c, HttpMethod.Post, "/api/v1/platform/tenants", platform.AccessToken, new
        {
            slug, name = "Pdf Other", adminUser = new { userName = "pdf.admin", displayName = "Other", password = "OtherAdmin1" },
        }), HttpStatusCode.Created);
        var otherAdmin = (await Login(_c, slug, "pdf.admin", "OtherAdmin1")).AccessToken;
        // A tenant-wide report-card reader in the other tenant still cannot reach demo's card.
        var role = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/roles", otherAdmin,
            new { code = "RcReader", name = "RcReader", permissions = new[] { "reportcards.read" } }), HttpStatusCode.Created);
        var user = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/users", otherAdmin,
            new { userName = "rc.reader", displayName = "Reader", password = "Reader123" }), HttpStatusCode.Created);
        await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/users/{Id(user)}/assignments", otherAdmin, new[] { new { roleId = Id(role), branchId = (Guid?)null } }));
        var reader = (await Login(_c, slug, "rc.reader", "Reader123")).AccessToken;
        await Problem(await Download($"/api/v1/report-cards/{x.SaraCard}/versions/1/pdf", reader), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Download($"/api/v1/portal/me/report-cards/{x.SaraCard}/pdf", reader), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Download($"/api/v1/report-cards/pdf-archive?classId={x.ClassId}", reader), HttpStatusCode.NotFound, "resource.not_found");
    }

    private async Task<string> RoleUserAsync(string userName, string roleCode, string[] permissions, string branchCode)
    {
        var admin = (await Demo(_c, "demo.admin")).AccessToken;
        var branch = Id((await Get(_c, "/api/v1/branches", admin))["items"]!.AsArray().First(b => b!["code"]!.GetValue<string>() == branchCode)!);
        var roles = (await Get(_c, "/api/v1/roles", admin)).AsArray();
        var role = roles.FirstOrDefault(r => r!["code"]!.GetValue<string>() == roleCode)
            ?? await Ok(await Send(_c, HttpMethod.Post, "/api/v1/roles", admin, new { code = roleCode, name = roleCode, permissions }), HttpStatusCode.Created);
        var users = (await Get(_c, $"/api/v1/users?search={userName}", admin))["items"]!.AsArray();
        var user = users.FirstOrDefault()
            ?? await Ok(await Send(_c, HttpMethod.Post, "/api/v1/users", admin, new { userName, displayName = userName, password = "Reader123" }), HttpStatusCode.Created);
        await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/users/{Id(user)}/assignments", admin, new[] { new { roleId = Id(role), branchId = (Guid?)branch } }));
        return (await Login(_c, "demo", userName, "Reader123")).AccessToken;
    }
}
