using System.Net;
using EEMP.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using static EEMP.IntegrationTests.Infrastructure.Api;

namespace EEMP.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthorizationTests(EempFactory factory)
{
    private readonly HttpClient _c = factory.CreateClient();

    [Theory]
    [InlineData("/api/v1/students")]
    [InlineData("/api/v1/branches")]
    [InlineData("/api/v1/users")]
    [InlineData("/api/v1/auth/me")]
    [InlineData("/api/v1/platform/tenants")]
    [InlineData("/api/v1/portal/me/profile")]
    public async Task Anonymous_requests_get_401(string url) =>
        await Problem(await Send(_c, HttpMethod.Get, url, null), HttpStatusCode.Unauthorized, "auth.unauthorized");

    [Fact]
    public async Task Tenant_admin_has_no_implicit_access_to_people_records()
    {
        var admin = await Demo(_c, "demo.admin");
        foreach (var url in new[] { "/api/v1/students", "/api/v1/guardians", "/api/v1/teachers", "/api/v1/staff" })
            await Problem(await Send(_c, HttpMethod.Get, url, admin.AccessToken), HttpStatusCode.Forbidden, "authorization.forbidden");
        await Get(_c, "/api/v1/users", admin.AccessToken);
        await Get(_c, "/api/v1/roles", admin.AccessToken);
    }

    [Fact]
    public async Task Missing_permission_is_forbidden()
    {
        var teacher = await Demo(_c, "teacher.one");
        await Problem(await Send(_c, HttpMethod.Get, "/api/v1/students", teacher.AccessToken), HttpStatusCode.Forbidden, "authorization.forbidden");
        var manager = await Demo(_c, "branch.manager");
        await Problem(await Send(_c, HttpMethod.Get, "/api/v1/users", manager.AccessToken), HttpStatusCode.Forbidden, "authorization.forbidden");
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/branches", manager.AccessToken, new { code = "X", name = "X" }),
            HttpStatusCode.Forbidden, "authorization.forbidden");
        await Problem(await Send(_c, HttpMethod.Get, "/api/v1/platform/tenants", manager.AccessToken), HttpStatusCode.Forbidden, "authorization.forbidden");
    }

    [Fact]
    public async Task Platform_admin_has_no_tenant_data_access()
    {
        var p = await Login(_c, null, "platform.admin", EempFactory.PlatformPassword);
        await Problem(await Send(_c, HttpMethod.Get, "/api/v1/branches", p.AccessToken), HttpStatusCode.Forbidden, "tenant.forbidden");
        await Problem(await Send(_c, HttpMethod.Get, "/api/v1/students", p.AccessToken), HttpStatusCode.Forbidden, "tenant.forbidden");
    }

    [Fact]
    public async Task Role_change_applies_on_next_request_and_self_grant_is_blocked()
    {
        var admin = await Demo(_c, "demo.admin");
        var user = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/users", admin.AccessToken,
            new { userName = "grant.test", displayName = "Grant", password = "GrantTest1" }), HttpStatusCode.Created);
        var session = await Login(_c, "demo", "grant.test", "GrantTest1");
        await Problem(await Send(_c, HttpMethod.Get, "/api/v1/roles", session.AccessToken), HttpStatusCode.Forbidden, "authorization.forbidden");

        var role = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/roles", admin.AccessToken,
            new { code = "RoleReader", name = "Role reader", permissions = new[] { "roles.read" } }), HttpStatusCode.Created);
        await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/users/{Id(user)}/assignments", admin.AccessToken, new[] { new { roleId = Id(role), branchId = (Guid?)null } }));
        await Get(_c, "/api/v1/roles", session.AccessToken); // same access token, new grant honoured

        await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/users/{Id(user)}/assignments", admin.AccessToken, Array.Empty<object>()));
        await Problem(await Send(_c, HttpMethod.Get, "/api/v1/roles", session.AccessToken), HttpStatusCode.Forbidden, "authorization.forbidden");

        var adminId = admin.User["id"]!.GetValue<string>();
        await Problem(await Send(_c, HttpMethod.Put, $"/api/v1/users/{adminId}/assignments", admin.AccessToken, Array.Empty<object>()),
            HttpStatusCode.Forbidden, "authorization.self_grant_forbidden");
    }

    [Fact]
    public async Task System_roles_are_immutable()
    {
        var admin = await Demo(_c, "demo.admin");
        var roles = (await Get(_c, "/api/v1/roles", admin.AccessToken)).AsArray();
        var system = roles.First(r => r!["isSystem"]!.GetValue<bool>())!;
        await Problem(await Send(_c, HttpMethod.Delete, $"/api/v1/roles/{Id(system)}", admin.AccessToken), HttpStatusCode.Conflict, "role.system_immutable");
    }

    [Fact]
    public async Task Every_api_endpoint_requires_authorization_except_auth_entry_points()
    {
        var allowAnonymous = new HashSet<string> { "/api/v1/auth/login", "/api/v1/auth/refresh", "/api/v1/auth/logout" };
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1", StringComparison.Ordinal) == true).ToList();
        Assert.NotEmpty(endpoints);
        foreach (var e in endpoints)
        {
            var path = e.RoutePattern.RawText!.TrimEnd('/');
            var anonymous = e.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var authorized = e.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0;
            if (allowAnonymous.Contains(path)) Assert.True(anonymous, path);
            else Assert.True(authorized && !anonymous, $"{path} is not protected");
        }
    }
}
