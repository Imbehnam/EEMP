using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EEMP.IntegrationTests.Infrastructure;

public sealed record Session(string AccessToken, string RefreshToken, JsonNode User);

/// <summary>Thin JSON helpers over the in-process client.</summary>
public static class Api
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<HttpResponseMessage> LoginRaw(HttpClient c, string? tenant, string user, string password) =>
        await c.PostAsJsonAsync("/api/v1/auth/login", new { tenantSlug = tenant, userName = user, password });

    public static async Task<Session> Login(HttpClient c, string? tenant, string user, string password)
    {
        var res = await LoginRaw(c, tenant, user, password);
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == HttpStatusCode.OK, $"login {user} failed: {(int)res.StatusCode} {body}");
        var node = JsonNode.Parse(body)!;
        return new Session(node["accessToken"]!.GetValue<string>(), node["refreshToken"]!.GetValue<string>(), node["user"]!);
    }

    public static Task<Session> Demo(HttpClient c, string user) =>
        Login(c, "demo", user, user == "demo.admin" ? EempFactory.AdminPassword : EempFactory.UserPassword);

    public static async Task<HttpResponseMessage> Send(HttpClient c, HttpMethod method, string url, string? token, object? body = null)
    {
        using var req = new HttpRequestMessage(method, url);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body, options: Json);
        return await c.SendAsync(req);
    }

    public static async Task<JsonNode> Ok(HttpResponseMessage res, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == expected, $"expected {(int)expected}, got {(int)res.StatusCode}: {body}");
        return string.IsNullOrEmpty(body) ? new JsonObject() : JsonNode.Parse(body)!;
    }

    public static async Task<JsonNode> Get(HttpClient c, string url, string token) => await Ok(await Send(c, HttpMethod.Get, url, token));

    /// <summary>Asserts a problem+json response with the given status and stable code.</summary>
    public static async Task Problem(HttpResponseMessage res, HttpStatusCode status, string code)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == status, $"expected {(int)status} {code}, got {(int)res.StatusCode}: {body}");
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
        var node = JsonNode.Parse(body)!;
        Assert.Equal(code, node["code"]!.GetValue<string>());
        Assert.NotNull(node["traceId"]);
    }

    public static Guid Id(JsonNode n) => Guid.Parse(n["id"]!.GetValue<string>());
}
