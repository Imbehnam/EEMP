using System.Net;
using System.Net.Http.Json;
using EEMP.IntegrationTests.Infrastructure;
using static EEMP.IntegrationTests.Infrastructure.Api;

namespace EEMP.IntegrationTests;

/// <summary>Regressions found by the M1 end-to-end run of the Next.js BFF against the real API (2026-11-24).</summary>
public class BehindBffRateLimitTests : IClassFixture<BehindBffFactory>
{
    private readonly HttpClient _c;

    public BehindBffRateLimitTests(BehindBffFactory factory) => _c = factory.CreateClient();

    private Task<HttpResponseMessage> LoginFrom(string clientIp)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { tenantSlug = "demo", userName = "nobody", password = "Nobody123" }),
        };
        req.Headers.Add("X-Forwarded-For", clientIp);
        return _c.SendAsync(req);
    }

    [Fact]
    public async Task Auth_rate_limit_is_per_browser_client_not_per_BFF_host()
    {
        // Two attempts allowed per client; the third from the same browser is limited ...
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginFrom("203.0.113.10")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginFrom("203.0.113.10")).StatusCode);
        await Problem(await LoginFrom("203.0.113.10"), HttpStatusCode.TooManyRequests, "rate_limit.exceeded");
        // ... while another user behind the same BFF (loopback) still gets through.
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginFrom("203.0.113.20")).StatusCode);
    }
}

[Collection(ApiCollection.Name)]
public class OpenApiContractTests(EempFactory factory)
{
    private readonly HttpClient _c = factory.CreateClient();

    [Fact]
    public async Task Paged_list_query_parameters_are_camelCase_in_openapi()
    {
        var doc = System.Text.Json.Nodes.JsonNode.Parse(await _c.GetStringAsync("/openapi/v1.json"))!;
        foreach (var path in new[] { "/api/v1/branches", "/api/v1/users", "/api/v1/students", "/api/v1/platform/tenants" })
        {
            var names = doc["paths"]![path]!["get"]!["parameters"]!.AsArray().Select(p => p!["name"]!.GetValue<string>()).ToList();
            Assert.Contains("page", names);
            Assert.Contains("pageSize", names);
            Assert.Contains("search", names);
        }
    }

    [Fact]
    public async Task Paging_still_binds_from_camelCase_query()
    {
        var s = await Demo(_c, "demo.admin");
        var page = await Get(_c, "/api/v1/branches?page=1&pageSize=1", s.AccessToken);
        Assert.Equal(1, page["pageSize"]!.GetValue<int>());
        Assert.Single(page["items"]!.AsArray());
    }
}
