using EEMP.Modules.Identity.Application;
using EEMP.Modules.Identity.Contracts;
using EEMP.Modules.Identity.Domain;
using EEMP.Modules.Organization.Application;
using EEMP.Modules.Organization.Domain;
using EEMP.Modules.People.Application;
using EEMP.Modules.People.Domain;
using EEMP.Modules.Tenancy.Application;
using EEMP.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EEMP.Api.Seeding;

/// <summary>
/// Development/test data. Runs only when Seed:Enabled is true (appsettings.Development.json) and every password is
/// supplied via configuration/environment (Seed__PlatformAdminPassword, Seed__DemoAdminPassword, Seed__DemoUserPassword).
/// No password is committed to source. Idempotent: skips anything that already exists.
/// </summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";
    public bool Enabled { get; set; }
    public string? PlatformAdminPassword { get; set; }
    public string? DemoAdminPassword { get; set; }
    public string? DemoUserPassword { get; set; }
}

public static class DevelopmentSeed
{
    public const string DemoSlug = "demo";
    public const string PlatformAdmin = "platform.admin";
    public const string DemoAdmin = "demo.admin";
    public const string BranchManager = "branch.manager";
    public const string TeacherUser = "teacher.one";
    public const string GuardianUser = "guardian.one";
    public const string StudentUser = "student.one";

    public static IServiceCollection AddDevelopmentSeed(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SeedOptions>().Bind(configuration.GetSection(SeedOptions.SectionName));
        return services;
    }

    public static async Task RunDevelopmentSeedAsync(this IServiceProvider root, CancellationToken ct = default)
    {
        var options = root.GetRequiredService<Microsoft.Extensions.Options.IOptions<SeedOptions>>().Value;
        var logger = root.GetRequiredService<ILoggerFactory>().CreateLogger("EEMP.Seed");
        if (!options.Enabled) return;
        if (string.IsNullOrEmpty(options.PlatformAdminPassword) || string.IsNullOrEmpty(options.DemoAdminPassword) || string.IsNullOrEmpty(options.DemoUserPassword))
        {
            logger.LogWarning("Seed is enabled but Seed__PlatformAdminPassword / Seed__DemoAdminPassword / Seed__DemoUserPassword are not all set; skipping seed.");
            return;
        }

        await using var scope = root.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var tenant = sp.GetRequiredService<ITenantContext>();
        var identity = sp.GetRequiredService<IIdentityDbContext>();
        var users = sp.GetRequiredService<UserManager<AppUser>>();

        using (tenant.BeginTenantScope(null))
        {
            var normalized = users.NormalizeName(PlatformAdmin);
            if (!await identity.Users.IgnoreQueryFilters().AnyAsync(u => u.TenantId == null && u.NormalizedUserName == normalized, ct))
            {
                await UserService.CreateUserAsync(null, new NewUserSpec(PlatformAdmin, "Platform Administrator", null, null,
                    options.PlatformAdminPassword, "en", "Asia/Tehran"), users, ct, isPlatformAdmin: true);
                logger.LogInformation("Seeded platform administrator");
            }
        }

        var tenants = sp.GetRequiredService<ITenancyDbContext>();
        var existing = await tenants.Tenants.FirstOrDefaultAsync(t => t.Slug == DemoSlug, ct);
        if (existing is not null) return;

        var created = await sp.GetRequiredService<TenantService>().CreateAsync(new CreateTenantRequest(
            DemoSlug, "Demo Institute", "fa-IR", null, "Asia/Tehran", "IRR",
            new TenantAdminUserRequest(DemoAdmin, "Demo Administrator", "admin@demo.invalid", null, options.DemoAdminPassword)), ct);

        using (tenant.BeginTenantScope(created.Id))
        {
            var org = sp.GetRequiredService<IOrganizationDbContext>();
            var north = new Branch { Code = "NORTH", Name = "North Branch", Address = "Tehran", Phone = "+98 21 0000 0001" };
            var south = new Branch { Code = "SOUTH", Name = "South Branch", Address = "Tehran", Phone = "+98 21 0000 0002" };
            org.Branches.AddRange(north, south);
            await org.SaveChangesAsync(ct);

            var roles = await identity.Roles.ToDictionaryAsync(r => r.Code, r => r.Id, ct);
            async Task<AppUser> User(string userName, string display, string role, Guid? branchId)
            {
                var u = await UserService.CreateUserAsync(created.Id, new NewUserSpec(userName, display, null, null, options.DemoUserPassword, "fa-IR", "Asia/Tehran"), users, ct);
                identity.RoleAssignments.Add(new RoleAssignment { UserId = u.Id, RoleId = roles[role], BranchId = branchId });
                await identity.SaveChangesAsync(ct);
                return u;
            }

            await User(BranchManager, "North Branch Manager", SystemRoles.BranchManager, north.Id);
            var teacherUser = await User(TeacherUser, "Teacher One", SystemRoles.Teacher, north.Id);
            var guardianUser = await User(GuardianUser, "Guardian One", SystemRoles.Guardian, north.Id);
            var studentUser = await User(StudentUser, "Student One", SystemRoles.Student, north.Id);

            var people = sp.GetRequiredService<IPeopleDbContext>();
            var guardian = new Guardian { BranchId = north.Id, FirstName = "Reza", LastName = "Ahmadi", Phone = "+98 912 000 0001", Gender = Gender.Male, UserId = guardianUser.Id };
            var student = new Student { BranchId = north.Id, FirstName = "Sara", LastName = "Ahmadi", StudentNumber = "S0001", BirthDate = new DateOnly(2012, 3, 21), Gender = Gender.Female, UserId = studentUser.Id };
            people.Guardians.Add(guardian);
            people.Students.Add(student);
            student.Guardians.Add(new StudentGuardian { StudentId = student.Id, GuardianId = guardian.Id, Relationship = GuardianRelationship.Father, IsPrimary = true });
            people.Students.Add(new Student { BranchId = north.Id, FirstName = "Ali", LastName = "Karimi", StudentNumber = "S0002" });
            people.Students.Add(new Student { BranchId = south.Id, FirstName = "Maryam", LastName = "Hosseini", StudentNumber = "S0003" });
            people.Teachers.Add(new Teacher { BranchId = north.Id, FirstName = "Mina", LastName = "Rahimi", EmployeeNumber = "T0001", Subjects = "Mathematics", UserId = teacherUser.Id });
            people.Teachers.Add(new Teacher { BranchId = south.Id, FirstName = "Hamid", LastName = "Moradi", EmployeeNumber = "T0002", Subjects = "Physics" });
            people.Staff.Add(new StaffMember { BranchId = north.Id, FirstName = "Leila", LastName = "Sadeghi", EmployeeNumber = "E0001", Position = "Receptionist" });
            await people.SaveChangesAsync(ct);
        }
        logger.LogInformation("Seeded demo tenant {Slug}", DemoSlug);
    }
}
