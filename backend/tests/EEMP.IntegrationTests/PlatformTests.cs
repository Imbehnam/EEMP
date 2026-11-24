using System.Net;
using System.Net.Http.Json;
using EEMP.IntegrationTests.Infrastructure;
using static EEMP.IntegrationTests.Infrastructure.Api;

namespace EEMP.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PlatformTests(EempFactory factory)
{
    private readonly HttpClient _c = factory.CreateClient();

    [Fact]
    public async Task Health_and_openapi_are_served()
    {
        Assert.Equal(HttpStatusCode.OK, (await _c.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _c.GetAsync("/health/ready")).StatusCode);
        var doc = await _c.GetStringAsync("/openapi/v1.json");
        Assert.Contains("/api/v1/students", doc, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Correlation_id_is_echoed_and_problem_title_is_localized()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        req.Headers.Add("X-Correlation-Id", "test-corr-1");
        req.Headers.Add("Accept-Language", "en");
        var res = await _c.SendAsync(req);
        Assert.Equal("test-corr-1", res.Headers.GetValues("X-Correlation-Id").Single());
        var body = await res.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
        Assert.Equal("You must sign in to continue.", body!["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task Duplicate_tenant_slug_conflicts_and_validation_errors_use_codes()
    {
        var p = await Login(_c, null, "platform.admin", EempFactory.PlatformPassword);
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/platform/tenants", p.AccessToken, new
        {
            slug = "demo", name = "Dup", adminUser = new { userName = "a", displayName = "A", password = "Abcdefg1" },
        }), HttpStatusCode.Conflict, "tenant.slug_taken");

        var res = await Send(_c, HttpMethod.Post, "/api/v1/platform/tenants", p.AccessToken, new { slug = "Bad Slug", name = "" });
        await Problem(res, HttpStatusCode.BadRequest, "validation.failed");
    }

    [Fact]
    public async Task Permissions_catalog_has_contract_shape()
    {
        var admin = await Demo(_c, "demo.admin");
        var perms = (await Get(_c, "/api/v1/permissions", admin.AccessToken)).AsArray();
        var students = perms.First(x => x!["code"]!.GetValue<string>() == "students.read")!;
        Assert.Equal("people", students["module"]!.GetValue<string>());
        Assert.True(students["isSensitive"]!.GetValue<bool>());
        Assert.True(students["branchScoped"]!.GetValue<bool>());
    }
}

public class RateLimitTests : IClassFixture<RateLimitedFactory>
{
    private readonly HttpClient _c;

    public RateLimitTests(RateLimitedFactory factory) => _c = factory.CreateClient();

    [Fact]
    public async Task Auth_endpoints_return_429_after_the_limit()
    {
        for (var i = 0; i < 3; i++)
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await LoginRaw(_c, "demo", "nobody", "Nobody123")).StatusCode);
        var res = await LoginRaw(_c, "demo", "nobody", "Nobody123");
        await Problem(res, HttpStatusCode.TooManyRequests, "rate_limit.exceeded");
        Assert.True(res.Headers.Contains("Retry-After"));
    }
}
