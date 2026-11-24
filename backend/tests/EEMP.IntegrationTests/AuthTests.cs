using System.Net;
using EEMP.IntegrationTests.Infrastructure;
using static EEMP.IntegrationTests.Infrastructure.Api;

namespace EEMP.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthTests(EempFactory factory)
{
    private readonly HttpClient _c = factory.CreateClient();

    [Fact]
    public async Task Login_returns_tokens_and_contract_user_shape()
    {
        var s = await Demo(_c, "branch.manager");
        Assert.False(string.IsNullOrEmpty(s.AccessToken));
        Assert.False(string.IsNullOrEmpty(s.RefreshToken));
        Assert.Equal("branch.manager", s.User["userName"]!.GetValue<string>());
        Assert.Equal("Demo Institute", s.User["tenantName"]!.GetValue<string>());
        Assert.False(s.User["isPlatformAdmin"]!.GetValue<bool>());
        Assert.Contains("students.read", s.User["permissions"]!.AsArray().Select(p => p!.GetValue<string>()));
        var scope = s.User["scopes"]!.AsArray().Single()!;
        Assert.Equal("BranchManager", scope["roleCode"]!.GetValue<string>());
        Assert.NotNull(scope["branchId"]);

        var me = await Get(_c, "/api/v1/auth/me", s.AccessToken);
        Assert.Equal(s.User["id"]!.GetValue<string>(), me["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Platform_admin_logs_in_without_tenant()
    {
        var s = await Login(_c, null, "platform.admin", EempFactory.PlatformPassword);
        Assert.True(s.User["isPlatformAdmin"]!.GetValue<bool>());
        Assert.Null(s.User["tenantId"]);
        Assert.Empty(s.User["permissions"]!.AsArray());
    }

    [Theory]
    [InlineData("demo", "nobody", "Whatever1")]
    [InlineData("no-such-tenant", "demo.admin", EempFactory.AdminPassword)]
    [InlineData(null, "demo.admin", EempFactory.AdminPassword)] // tenant users cannot log in as platform
    public async Task Bad_credentials_are_indistinguishable(string? tenant, string user, string password) =>
        await Problem(await LoginRaw(_c, tenant, user, password), HttpStatusCode.Unauthorized, "auth.invalid_credentials");

    [Fact]
    public async Task Refresh_rotates_and_reuse_revokes_the_whole_session()
    {
        var s = await Demo(_c, "teacher.one");
        var first = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/auth/refresh", null, new { refreshToken = s.RefreshToken }));
        var rotated = first["refreshToken"]!.GetValue<string>();
        Assert.NotEqual(s.RefreshToken, rotated);
        var newAccess = first["accessToken"]!.GetValue<string>();
        await Get(_c, "/api/v1/auth/me", newAccess);

        // Replaying the consumed token is treated as theft: the family is revoked.
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/auth/refresh", null, new { refreshToken = s.RefreshToken }),
            HttpStatusCode.Unauthorized, "auth.refresh_token_reused");
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/auth/refresh", null, new { refreshToken = rotated }),
            HttpStatusCode.Unauthorized, "auth.invalid_refresh_token");
        await Problem(await Send(_c, HttpMethod.Get, "/api/v1/auth/me", newAccess), HttpStatusCode.Unauthorized, "auth.session_revoked");
    }

    [Fact]
    public async Task Logout_revokes_access_and_refresh_immediately()
    {
        var s = await Demo(_c, "teacher.one");
        await Ok(await Send(_c, HttpMethod.Post, "/api/v1/auth/logout", null, new { refreshToken = s.RefreshToken }), HttpStatusCode.NoContent);
        await Problem(await Send(_c, HttpMethod.Get, "/api/v1/auth/me", s.AccessToken), HttpStatusCode.Unauthorized, "auth.session_revoked");
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/auth/refresh", null, new { refreshToken = s.RefreshToken }),
            HttpStatusCode.Unauthorized, "auth.invalid_refresh_token");
    }

    [Fact]
    public async Task Deactivation_revokes_sessions_and_blocks_login()
    {
        var admin = await Demo(_c, "demo.admin");
        var created = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/users", admin.AccessToken,
            new { userName = "temp.user", displayName = "Temp", password = "TempUser1" }), HttpStatusCode.Created);
        var user = await Login(_c, "demo", "temp.user", "TempUser1");
        await Get(_c, "/api/v1/auth/me", user.AccessToken);

        await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/users/{Id(created)}/deactivate", admin.AccessToken));
        await Problem(await Send(_c, HttpMethod.Get, "/api/v1/auth/me", user.AccessToken), HttpStatusCode.Unauthorized, "auth.session_revoked");
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/auth/refresh", null, new { refreshToken = user.RefreshToken }),
            HttpStatusCode.Unauthorized, "auth.invalid_refresh_token");
        await Problem(await LoginRaw(_c, "demo", "temp.user", "TempUser1"), HttpStatusCode.Forbidden, "auth.account_inactive");

        await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/users/{Id(created)}/activate", admin.AccessToken));
        await Login(_c, "demo", "temp.user", "TempUser1");
    }

    [Fact]
    public async Task Preferences_update_locale_and_timezone()
    {
        var s = await Demo(_c, "student.one");
        var me = await Ok(await Send(_c, HttpMethod.Put, "/api/v1/auth/me/preferences", s.AccessToken, new { locale = "en", timeZone = "Europe/Istanbul" }));
        Assert.Equal("en", me["locale"]!.GetValue<string>());
        await Problem(await Send(_c, HttpMethod.Put, "/api/v1/auth/me/preferences", s.AccessToken, new { locale = "de", timeZone = "UTC" }),
            HttpStatusCode.BadRequest, "validation.failed");
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var s = await Demo(_c, "student.one");
        await Problem(await Send(_c, HttpMethod.Get, "/api/v1/auth/me", s.AccessToken[..^4] + "AAAA"), HttpStatusCode.Unauthorized, "auth.unauthorized");
    }
}
