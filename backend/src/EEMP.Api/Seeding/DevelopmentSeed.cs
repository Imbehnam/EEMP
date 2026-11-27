using EEMP.Modules.Academic.Application;
using EEMP.Modules.Academic.Domain;
using EEMP.Modules.Finance.Application;
using EEMP.Modules.Finance.Domain;
using EEMP.Modules.Identity.Application;
using EEMP.Modules.Identity.Contracts;
using EEMP.Modules.Identity.Domain;
using EEMP.Modules.Organization.Application;
using EEMP.Modules.Organization.Domain;
using EEMP.Modules.People.Application;
using EEMP.Modules.People.Domain;
using EEMP.Modules.Tenancy.Application;
using EEMP.Modules.Workflow.Application;
using EEMP.Modules.Workflow.Domain;
using EEMP.SharedKernel.Authorization;
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
    public const string FinanceOfficerUser = "finance.officer";

    public static IServiceCollection AddDevelopmentSeed(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SeedOptions>().Bind(configuration.GetSection(SeedOptions.SectionName));
        return services;
    }

    public static async Task RunDevelopmentSeedAsync(this IServiceProvider root, CancellationToken ct = default)
    {
        var options = root.GetRequiredService<Microsoft.Extensions.Options.IOptions<SeedOptions>>().Value;
        var logger = root.GetRequiredService<ILoggerFactory>().CreateLogger("EEMP.Seed");
        if (!options.Enabled)
        {
            // Seed:Enabled normally comes from appsettings.Development.json, which is read from the content root
            // (the working directory). Starting the API from another directory silently drops it, so say so when
            // the seed passwords show the caller clearly expected a seed.
            if (!string.IsNullOrEmpty(options.PlatformAdminPassword) || !string.IsNullOrEmpty(options.DemoAdminPassword) || !string.IsNullOrEmpty(options.DemoUserPassword))
            {
                var contentRoot = root.GetService<IHostEnvironment>()?.ContentRootPath;
                logger.LogWarning("Seed passwords are configured but Seed:Enabled is false, so the development seed did not run. " +
                    "Seed:Enabled is set in appsettings.Development.json under the content root {ContentRoot}; start the API from its " +
                    "output directory or set Seed__Enabled=true.", contentRoot);
            }
            return;
        }
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
        if (existing is not null)
        {
            // Databases seeded before milestone 2 get the academic sample data once.
            using (tenant.BeginTenantScope(existing.Id))
            {
                await SeedAcademicAsync(sp, logger, ct);
                await SeedFinanceAsync(sp, existing.Id, options.DemoUserPassword, logger, ct);
                await SeedWorkflowAsync(sp, logger, ct);
            }
            return;
        }

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
            await SeedAcademicAsync(sp, logger, ct);
            await SeedFinanceAsync(sp, created.Id, options.DemoUserPassword, logger, ct);
            await SeedWorkflowAsync(sp, logger, ct);
        }
        logger.LogInformation("Seeded demo tenant {Slug}", DemoSlug);
    }

    /// <summary>
    /// Milestone 2 sample data (runs inside the demo tenant scope; skipped when any academic year exists):
    /// year 1405-1406 with two terms, subjects/courses, built-in grading schemes, MATH-7A (North, teacher.one) and
    /// PHYS-7S (South), enrollments, one attendance sheet, a published quiz and a draft midterm.
    /// </summary>
    private static async Task SeedAcademicAsync(IServiceProvider sp, ILogger logger, CancellationToken ct)
    {
        var db = sp.GetRequiredService<IAcademicDbContext>();
        if (await db.AcademicYears.AnyAsync(ct)) return;
        var org = sp.GetRequiredService<IOrganizationDbContext>();
        var people = sp.GetRequiredService<IPeopleDbContext>();
        var north = await org.Branches.FirstOrDefaultAsync(b => b.Code == "NORTH", ct);
        var south = await org.Branches.FirstOrDefaultAsync(b => b.Code == "SOUTH", ct);
        var students = await people.Students.ToDictionaryAsync(x => x.StudentNumber, ct);
        var teachers = await people.Teachers.ToDictionaryAsync(x => x.EmployeeNumber, ct);
        if (north is null || south is null || !students.TryGetValue("S0001", out var sara) || !students.TryGetValue("S0002", out var ali)
            || !students.TryGetValue("S0003", out var maryam) || !teachers.TryGetValue("T0001", out var mina) || !teachers.TryGetValue("T0002", out var hamid))
        {
            logger.LogWarning("Demo tenant lacks the M1 sample branches/people; skipping academic seed.");
            return;
        }

        await sp.GetRequiredService<CatalogService>().EnsureDefaultSchemesAsync(ct);
        var ir20 = await db.GradingSchemes.FirstAsync(x => x.Code == DefaultGradingSchemes.Iranian20, ct);

        var year = new AcademicYear { Name = "1405-1406", StartDate = new DateOnly(2026, 9, 23), EndDate = new DateOnly(2027, 6, 21), Status = AcademicYearStatus.Active };
        var term1 = new Term { AcademicYearId = year.Id, Name = "Term 1", StartDate = new DateOnly(2026, 9, 23), EndDate = new DateOnly(2027, 1, 20), SortOrder = 1 };
        var term2 = new Term { AcademicYearId = year.Id, Name = "Term 2", StartDate = new DateOnly(2027, 1, 21), EndDate = new DateOnly(2027, 6, 21), SortOrder = 2 };
        var math = new Subject { Code = "MATH", Name = "Mathematics" };
        var phys = new Subject { Code = "PHYS", Name = "Physics" };
        var lit = new Subject { Code = "LIT", Name = "Persian Literature" };
        var math7 = new Course { SubjectId = math.Id, Code = "MATH-7", Name = "Mathematics 7", GradeLevel = "7", GradingSchemeId = ir20.Id };
        var phys7 = new Course { SubjectId = phys.Id, Code = "PHYS-7", Name = "Physics 7", GradeLevel = "7", GradingSchemeId = ir20.Id };
        db.AcademicYears.Add(year);
        db.Terms.AddRange(term1, term2);
        db.Subjects.AddRange(math, phys, lit);
        db.Courses.AddRange(math7, phys7);
        await db.SaveChangesAsync(ct);

        var mathA = new ClassSection
        {
            TermId = term1.Id, CourseId = math7.Id, BranchId = north.Id, Code = "MATH-7A", Name = "Mathematics 7 - A", Capacity = 30,
            Status = ClassStatus.Active, GradingSchemeId = ir20.Id, DefaultRoom = "101",
        };
        mathA.Schedule.Add(new ScheduleSlot { ClassId = mathA.Id, Weekday = DayOfWeek.Saturday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(9, 30) });
        mathA.Schedule.Add(new ScheduleSlot { ClassId = mathA.Id, Weekday = DayOfWeek.Monday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(9, 30) });
        mathA.Teachers.Add(new TeacherAssignment { ClassId = mathA.Id, TeacherId = mina.Id, Role = TeacherRole.Primary });
        var physS = new ClassSection
        {
            TermId = term1.Id, CourseId = phys7.Id, BranchId = south.Id, Code = "PHYS-7S", Name = "Physics 7 - South", Capacity = 25,
            Status = ClassStatus.Active, GradingSchemeId = ir20.Id, DefaultRoom = "201",
        };
        physS.Schedule.Add(new ScheduleSlot { ClassId = physS.Id, Weekday = DayOfWeek.Sunday, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 30) });
        physS.Teachers.Add(new TeacherAssignment { ClassId = physS.Id, TeacherId = hamid.Id, Role = TeacherRole.Primary });
        db.Classes.AddRange(mathA, physS);

        var enrolledOn = new DateOnly(2026, 9, 23);
        db.Enrollments.Add(new Enrollment { ClassId = mathA.Id, StudentId = sara.Id, EnrolledOn = enrolledOn });
        db.Enrollments.Add(new Enrollment { ClassId = mathA.Id, StudentId = ali.Id, EnrolledOn = enrolledOn });
        db.Enrollments.Add(new Enrollment { ClassId = physS.Id, StudentId = maryam.Id, EnrolledOn = enrolledOn });
        mathA.EnrolledCount = 2;
        physS.EnrolledCount = 1;

        var markedAt = DateTime.UtcNow;
        var session = new AttendanceSession { ClassId = mathA.Id, Date = new DateOnly(2026, 11, 21), ScheduleSlotId = mathA.Schedule[0].Id };
        session.Records.Add(new AttendanceRecord { SessionId = session.Id, StudentId = sara.Id, Status = AttendanceStatus.Present, MarkedAt = markedAt });
        session.Records.Add(new AttendanceRecord { SessionId = session.Id, StudentId = ali.Id, Status = AttendanceStatus.Late, Note = "10 min", MarkedAt = markedAt });
        db.AttendanceSessions.Add(session);

        var quiz = new Assessment { ClassId = mathA.Id, Name = "Quiz 1", Type = AssessmentType.Quiz, Weight = 1, MaxScore = 20, Date = new DateOnly(2026, 10, 20) };
        quiz.Results.Add(new AssessmentResult { AssessmentId = quiz.Id, StudentId = sara.Id, Mark = ResultMark.Scored, Score = 18.5m });
        quiz.Results.Add(new AssessmentResult { AssessmentId = quiz.Id, StudentId = ali.Id, Mark = ResultMark.Scored, Score = 15m });
        var now = DateTime.UtcNow;
        AssessmentWorkflow.Apply(quiz, GradeAuditAction.Submitted, now, null);
        AssessmentWorkflow.Apply(quiz, GradeAuditAction.Approved, now, null);
        AssessmentWorkflow.Apply(quiz, GradeAuditAction.Published, now, null);
        var midterm = new Assessment { ClassId = mathA.Id, Name = "Midterm", Type = AssessmentType.Midterm, Weight = 2, MaxScore = 20, Date = new DateOnly(2026, 11, 22) };
        midterm.Results.Add(new AssessmentResult { AssessmentId = midterm.Id, StudentId = sara.Id, Mark = ResultMark.Scored, Score = 17m });
        db.Assessments.AddRange(quiz, midterm);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demo academic data");
    }

    /// <summary>
    /// Milestone 3 sample data (demo tenant scope; skipped when any fee type exists): user finance.officer (FinanceOfficer, tenant-wide),
    /// fee type TUITION, fee item "Tuition - Term 1" (12,000,000 IRR), an issued two-installment invoice for Sara's MATH-7A enrollment
    /// (student.one / guardian.one) and a 5,000,000 IRR cash payment with receipt.
    /// </summary>
    private static async Task SeedFinanceAsync(IServiceProvider sp, Guid tenantId, string password, ILogger logger, CancellationToken ct)
    {
        var fin = sp.GetRequiredService<IFinanceDbContext>();
        if (await fin.FeeTypes.AnyAsync(ct)) return;
        var identity = sp.GetRequiredService<IIdentityDbContext>();
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var role = await identity.Roles.FirstOrDefaultAsync(r => r.Code == SystemRoles.FinanceOfficer, ct);
        var normalized = users.NormalizeName(FinanceOfficerUser);
        if (role is not null && !await identity.Users.AnyAsync(u => u.NormalizedUserName == normalized, ct))
        {
            var u = await UserService.CreateUserAsync(tenantId, new NewUserSpec(FinanceOfficerUser, "Finance Officer", null, null, password, "fa-IR", "Asia/Tehran"), users, ct);
            identity.RoleAssignments.Add(new RoleAssignment { UserId = u.Id, RoleId = role.Id, BranchId = null });
            await identity.SaveChangesAsync(ct);
        }

        var academic = sp.GetRequiredService<IAcademicDbContext>();
        var people = sp.GetRequiredService<IPeopleDbContext>();
        var sara = await people.Students.FirstOrDefaultAsync(x => x.StudentNumber == "S0001", ct);
        var mathA = await academic.Classes.FirstOrDefaultAsync(c => c.Code == "MATH-7A", ct);
        var enrollment = sara is null || mathA is null ? null
            : await academic.Enrollments.FirstOrDefaultAsync(e => e.ClassId == mathA.Id && e.StudentId == sara.Id, ct);

        var tuition = new FeeType { Code = "TUITION", Name = "Tuition", Taxable = false };
        fin.FeeTypes.Add(tuition);
        var fee = new FeeItem { FeeTypeId = tuition.Id, Name = "Tuition - Term 1", Amount = 12_000_000m, Currency = "IRR", TermId = mathA?.TermId };
        fin.FeeItems.Add(fee);
        fin.Settings.Add(new FinanceSettings { OnlinePaymentsEnabled = true });
        if (sara is null || mathA is null || enrollment is null)
        {
            await fin.SaveChangesAsync(ct);
            logger.LogWarning("Demo tenant lacks the M2 sample enrollment; seeded finance setup only.");
            return;
        }

        var now = DateTime.UtcNow;
        var issueDate = new DateOnly(2026, 9, 23);
        var inv = new Invoice
        {
            Number = DocumentNumbers.Format(DocumentNumbers.Invoice, 1), StudentId = sara.Id, BranchId = mathA.BranchId, TermId = mathA.TermId,
            Status = InvoiceStatus.Issued, Currency = "IRR", IssueDate = issueDate, IssuedAt = now,
        };
        inv.Lines.Add(new InvoiceLine { InvoiceId = inv.Id, FeeItemId = fee.Id, EnrollmentId = enrollment.Id, Description = fee.Name, Quantity = 1, UnitPrice = fee.Amount });
        InvoiceCalculator.Recalculate(inv);
        var seq = 1;
        foreach (var (due, amount) in InstallmentPlanner.EqualSplit(inv.Total, 2, new DateOnly(2026, 10, 22), 60, inv.Currency))
            inv.Installments.Add(new Installment { InvoiceId = inv.Id, SeqNo = seq++, DueDate = due, Amount = amount });
        inv.DueDate = inv.Installments[^1].DueDate;
        fin.Invoices.Add(inv);
        fin.GeneratedCharges.Add(new GeneratedCharge { EnrollmentId = enrollment.Id, FeeItemId = fee.Id, InvoiceId = inv.Id });

        var payment = new Payment
        {
            StudentId = sara.Id, BranchId = mathA.BranchId, Method = PaymentMethod.Cash, Status = PaymentStatus.Succeeded, Amount = 5_000_000m, Currency = "IRR",
            ReceivedOn = new DateOnly(2026, 10, 1), ReceiptNumber = DocumentNumbers.Format(DocumentNumbers.Receipt, 1), SucceededAt = now, TargetInvoiceId = inv.Id,
        };
        var (lines, _) = Allocator.Allocate(payment.Amount, [new AllocationTarget(inv.Id, inv.Balance, inv.Installments.Select(i => (i.Id, i.DueDate, i.Remaining)).ToList())]);
        foreach (var l in lines)
        {
            payment.Allocations.Add(new PaymentAllocation { PaymentId = payment.Id, InvoiceId = inv.Id, InstallmentId = l.InstallmentId, Amount = l.Amount, CreatedAt = now });
            inv.Installments.First(i => i.Id == l.InstallmentId).PaidAmount += l.Amount;
            inv.PaidAmount += l.Amount;
            payment.Allocated += l.Amount;
        }
        InvoiceCalculator.UpdatePaymentStatus(inv);
        fin.Payments.Add(payment);
        fin.NumberSeries.Add(new NumberSeries { Series = DocumentNumbers.Invoice, LastValue = 1 });
        fin.NumberSeries.Add(new NumberSeries { Series = DocumentNumbers.Receipt, LastValue = 1 });
        fin.Audit.Add(new FinanceAuditEntry { At = now, Action = "seed.invoice", EntityType = "invoice", EntityId = inv.Id, InvoiceId = inv.Id, StudentId = sara.Id, Amount = inv.Total, Currency = "IRR" });
        fin.Audit.Add(new FinanceAuditEntry { At = now, Action = "seed.payment", EntityType = "payment", EntityId = payment.Id, InvoiceId = inv.Id, PaymentId = payment.Id, StudentId = sara.Id, Amount = payment.Amount, Currency = "IRR" });
        await fin.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demo finance data");
    public const string DiscountApprovalKey = "discount-approval";

    /// <summary>
    /// EEMP-011 sample definition (demo tenant; skipped when it exists): "discount-approval" for finance discounts, version 1
    /// published, one step approved by a holder of workflow.tasks.approve in the instance branch within 48 hours.
    /// </summary>
    private static async Task SeedWorkflowAsync(IServiceProvider sp, ILogger logger, CancellationToken ct)
    {
        var db = sp.GetRequiredService<IWorkflowDbContext>();
        if (await db.Definitions.AnyAsync(d => d.Key == DiscountApprovalKey, ct)) return;
        var def = new WorkflowDefinition { Key = DiscountApprovalKey, Name = "Discount approval", SubjectModule = "finance", SubjectType = "discount", PublishedVersion = 1 };
        var version = new WorkflowDefinitionVersion
        {
            DefinitionId = def.Id, VersionNo = 1, Status = DefinitionVersionStatus.Published, RequireDistinctApprovers = true,
            PublishedAt = DateTime.UtcNow,
        };
        version.Steps.Add(new WorkflowStepDefinition
        {
            VersionId = version.Id, Order = 1, Name = "Branch manager approval", Permission = Permissions.WorkflowTasksApprove, SlaHours = 48, AllowReturn = true,
        });
        def.Versions.Add(version);
        db.Definitions.Add(def);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded workflow definition {Key}", DiscountApprovalKey);
    }
}
