using System.Net;
using System.Text.Json.Nodes;
using EEMP.IntegrationTests.Infrastructure;
using static EEMP.IntegrationTests.Infrastructure.Api;

namespace EEMP.IntegrationTests;

[Collection(ApiCollection.Name)]
public class IsolationTests(EempFactory factory)
{
    private readonly HttpClient _c = factory.CreateClient();

    private async Task<(Guid North, Guid South)> Branches(string adminToken)
    {
        var items = (await Get(_c, "/api/v1/branches", adminToken))["items"]!.AsArray();
        Guid Find(string code) => Id(items.First(b => b!["code"]!.GetValue<string>() == code)!);
        return (Find("NORTH"), Find("SOUTH"));
    }

    [Fact]
    public async Task Branch_manager_sees_only_own_branch()
    {
        var admin = await Demo(_c, "demo.admin");
        var (north, south) = await Branches(admin.AccessToken);
        var m = await Demo(_c, "branch.manager");

        var students = (await Get(_c, "/api/v1/students?pageSize=100", m.AccessToken))["items"]!.AsArray();
        Assert.NotEmpty(students);
        Assert.All(students, s => Assert.Equal(north, Guid.Parse(s!["branchId"]!.GetValue<string>())));
        var filtered = await Get(_c, $"/api/v1/students?branchId={south}", m.AccessToken);
        Assert.Equal(0, filtered["totalCount"]!.GetValue<int>());

        var branches = (await Get(_c, "/api/v1/branches", m.AccessToken))["items"]!.AsArray();
        Assert.Equal([north], branches.Select(b => Id(b!)).ToArray());
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/branches/{south}", m.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
    }

    [Fact]
    public async Task Sibling_branch_records_are_not_found_and_cannot_be_created()
    {
        var admin = await Demo(_c, "demo.admin");
        var (_, south) = await Branches(admin.AccessToken);
        var m = await Demo(_c, "branch.manager");

        // Elevate a test user to read the south student id without granting the manager anything.
        var southStudentId = await SouthStudentIdAsync(admin.AccessToken, south);
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/students/{southStudentId}", m.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Delete, $"/api/v1/students/{southStudentId}", m.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/students", m.AccessToken, new { branchId = south, firstName = "X", lastName = "Y" }),
            HttpStatusCode.Forbidden, "authorization.forbidden_scope");
    }

    private async Task<Guid> SouthStudentIdAsync(string adminToken, Guid south)
    {
        var auditor = await EnsureUserWithRoleAsync(adminToken, "south.reader", "SouthReader", ["students.read"], south);
        var students = (await Get(_c, "/api/v1/students", auditor))["items"]!.AsArray();
        return Id(students.First()!);
    }

    private async Task<string> EnsureUserWithRoleAsync(string adminToken, string userName, string roleCode, string[] permissions, Guid? branchId)
    {
        var roles = (await Get(_c, "/api/v1/roles", adminToken)).AsArray();
        var role = roles.FirstOrDefault(r => r!["code"]!.GetValue<string>() == roleCode)
            ?? await Ok(await Send(_c, HttpMethod.Post, "/api/v1/roles", adminToken, new { code = roleCode, name = roleCode, permissions }), HttpStatusCode.Created);
        var users = (await Get(_c, $"/api/v1/users?search={userName}", adminToken))["items"]!.AsArray();
        var user = users.FirstOrDefault()
            ?? await Ok(await Send(_c, HttpMethod.Post, "/api/v1/users", adminToken, new { userName, displayName = userName, password = "Reader123" }), HttpStatusCode.Created);
        await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/users/{Id(user)}/assignments", adminToken, new[] { new { roleId = Id(role), branchId } }));
        return (await Login(_c, "demo", userName, "Reader123")).AccessToken;
    }

    [Fact]
    public async Task Tenants_cannot_see_each_others_data()
    {
        var platform = await Login(_c, null, "platform.admin", EempFactory.PlatformPassword);
        var slug = $"t{Guid.NewGuid():N}"[..12];
        var tenant = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/platform/tenants", platform.AccessToken, new
        {
            slug, name = "Other Institute",
            adminUser = new { userName = "other.admin", displayName = "Other Admin", password = "OtherAdmin1" },
        }), HttpStatusCode.Created);
        Assert.Equal("active", tenant["status"]!.GetValue<string>());

        var other = await Login(_c, slug, "other.admin", "OtherAdmin1");
        var demo = await Demo(_c, "demo.admin");
        var (north, _) = await Branches(demo.AccessToken);
        var demoUserId = demo.User["id"]!.GetValue<string>();
        var demoRole = (await Get(_c, "/api/v1/roles", demo.AccessToken)).AsArray().First()!;

        // The other tenant's admin cannot read or touch demo resources by id: they simply do not exist for it.
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/branches/{north}", other.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/users/{demoUserId}", other.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/roles/{Id(demoRole)}", other.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Delete, $"/api/v1/branches/{north}", other.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
        Assert.Equal(0, (await Get(_c, "/api/v1/branches", other.AccessToken))["totalCount"]!.GetValue<int>());
        Assert.DoesNotContain((await Get(_c, "/api/v1/users", other.AccessToken))["items"]!.AsArray(), u => u!["userName"]!.GetValue<string>() == "demo.admin");

        // A cross-tenant branch id cannot be smuggled into an assignment.
        var otherUser = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/users", other.AccessToken,
            new { userName = "x.user", displayName = "X", password = "XUser1234" }), HttpStatusCode.Created);
        var otherRole = (await Get(_c, "/api/v1/roles", other.AccessToken)).AsArray().First()!;
        var res = await Send(_c, HttpMethod.Put, $"/api/v1/users/{Id(otherUser)}/assignments", other.AccessToken, new[] { new { roleId = Id(otherRole), branchId = (Guid?)north } });
        await Problem(res, HttpStatusCode.BadRequest, "validation.failed");

        // Tenant comes only from the token: the same user name in another tenant is a different identity.
        await Problem(await LoginRaw(_c, slug, "demo.admin", EempFactory.AdminPassword), HttpStatusCode.Unauthorized, "auth.invalid_credentials");

        // Suspension blocks the tenant's sessions immediately.
        await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/platform/tenants/{Id(tenant)}/suspend", platform.AccessToken));
        await Problem(await Send(_c, HttpMethod.Get, "/api/v1/branches", other.AccessToken), HttpStatusCode.Unauthorized, "tenant.suspended");
        await Problem(await LoginRaw(_c, slug, "other.admin", "OtherAdmin1"), HttpStatusCode.Forbidden, "tenant.suspended");
    }

    [Fact]
    public async Task Portal_shows_only_own_profile_and_linked_students()
    {
        var g = await Demo(_c, "guardian.one");
        var profile = await Get(_c, "/api/v1/portal/me/profile", g.AccessToken);
        Assert.Equal("guardian", profile["kind"]!.GetValue<string>());
        var students = (await Get(_c, "/api/v1/portal/me/students", g.AccessToken)).AsArray();
        var only = Assert.Single(students)!;
        Assert.Equal("S0001", only["studentNumber"]!.GetValue<string>());
        Assert.Equal("father", only["relationship"]!.GetValue<string>());

        var s = await Demo(_c, "student.one");
        var sp = await Get(_c, "/api/v1/portal/me/profile", s.AccessToken);
        Assert.Equal("student", sp["kind"]!.GetValue<string>());
        Assert.Equal("female", sp["profile"]!["gender"]!.GetValue<string>());
        Assert.Empty((await Get(_c, "/api/v1/portal/me/students", s.AccessToken)).AsArray());

        var admin = await Demo(_c, "demo.admin");
        await Problem(await Send(_c, HttpMethod.Get, "/api/v1/portal/me/profile", admin.AccessToken), HttpStatusCode.NotFound, "portal.profile_not_found");
    }

    [Fact]
    public async Task People_crud_with_guardian_links_and_stale_rowversion_conflict()
    {
        var m = await Demo(_c, "branch.manager");
        var north = Guid.Parse(m.User["scopes"]!.AsArray()[0]!["branchId"]!.GetValue<string>());
        var guardian = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/guardians", m.AccessToken,
            new { branchId = north, firstName = "Narges", lastName = "Kazemi", gender = "female", phone = "+98 912 111 2222" }), HttpStatusCode.Created);
        var student = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/students", m.AccessToken, new
        {
            branchId = north, firstName = "Omid", lastName = "Kazemi", birthDate = "2013-05-01", gender = "male",
            guardians = new[] { new { guardianId = Id(guardian), relationship = "mother", isPrimary = true } },
        }), HttpStatusCode.Created);
        Assert.StartsWith("S", student["studentNumber"]!.GetValue<string>());
        Assert.Equal("active", student["status"]!.GetValue<string>());
        Assert.Equal("2013-05-01", student["birthDate"]!.GetValue<string>());

        var links = (await Get(_c, $"/api/v1/students/{Id(student)}/guardians", m.AccessToken)).AsArray();
        Assert.Equal("Narges", Assert.Single(links)!["firstName"]!.GetValue<string>());

        var staleRowVersion = student["rowVersion"]!.GetValue<string>();
        var update = new
        {
            branchId = north, firstName = "Omid", lastName = "Kazemi-Updated", studentNumber = student["studentNumber"]!.GetValue<string>(),
            status = "inactive", rowVersion = staleRowVersion,
        };
        var updated = await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/students/{Id(student)}", m.AccessToken, update));
        Assert.Equal("inactive", updated["status"]!.GetValue<string>());
        Assert.NotEqual(staleRowVersion, updated["rowVersion"]!.GetValue<string>());
        await Problem(await Send(_c, HttpMethod.Put, $"/api/v1/students/{Id(student)}", m.AccessToken, update), HttpStatusCode.Conflict, "concurrency.conflict");

        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/students", m.AccessToken, new { branchId = north, firstName = "", lastName = "X", gender = "robot" }),
            HttpStatusCode.BadRequest, "validation.failed");

        await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/students/{Id(student)}/guardians", m.AccessToken, Array.Empty<object>()));
        await Ok(await Send(_c, HttpMethod.Delete, $"/api/v1/students/{Id(student)}", m.AccessToken), HttpStatusCode.NoContent);
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/students/{Id(student)}", m.AccessToken), HttpStatusCode.NotFound, "resource.not_found");
    }

    [Fact]
    public async Task Branch_update_with_stale_rowversion_returns_409()
    {
        var admin = await Demo(_c, "demo.admin");
        var created = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/branches", admin.AccessToken,
            new { code = $"B{Random.Shared.Next(1000, 9999)}", name = "Temp Branch" }), HttpStatusCode.Created);
        var body = new JsonObject
        {
            ["code"] = created["code"]!.GetValue<string>(), ["name"] = "Renamed", ["isActive"] = true,
            ["rowVersion"] = created["rowVersion"]!.GetValue<string>(),
        };
        await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/branches/{Id(created)}", admin.AccessToken, body));
        await Problem(await Send(_c, HttpMethod.Put, $"/api/v1/branches/{Id(created)}", admin.AccessToken, body), HttpStatusCode.Conflict, "concurrency.conflict");
        await Ok(await Send(_c, HttpMethod.Delete, $"/api/v1/branches/{Id(created)}", admin.AccessToken), HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Organization_read_and_update()
    {
        var admin = await Demo(_c, "demo.admin");
        var org = await Get(_c, "/api/v1/organization", admin.AccessToken);
        Assert.Equal("Demo Institute", org["name"]!.GetValue<string>());
        var updated = await Ok(await Send(_c, HttpMethod.Put, "/api/v1/organization", admin.AccessToken, new
        {
            name = "Demo Institute", legalName = "Demo Institute Co.", defaultLocale = "fa-IR", timeZone = "Asia/Tehran",
            rowVersion = org["rowVersion"]!.GetValue<string>(),
        }));
        Assert.Equal("Demo Institute Co.", updated["legalName"]!.GetValue<string>());
    }
}
