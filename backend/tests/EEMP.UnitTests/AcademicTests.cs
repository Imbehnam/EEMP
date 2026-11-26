using EEMP.Infrastructure.Tenancy;
using EEMP.Modules.Academic.Application;
using EEMP.Modules.Academic.Domain;
using EEMP.Modules.Academic.Infrastructure;
using EEMP.Modules.Organization.Contracts;
using EEMP.Modules.People.Contracts;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EEMP.UnitTests;

public class GradeCalculatorTests
{
    private static readonly SchemeSpec Ir20 = GradeCalculator.Spec(DefaultGradingSchemes.Create().Single(s => s.Code == DefaultGradingSchemes.Iranian20));
    private static readonly SchemeSpec Letter = GradeCalculator.Spec(DefaultGradingSchemes.Create().Single(s => s.Code == DefaultGradingSchemes.Letter));

    [Fact]
    public void Weighted_average_is_scaled_to_the_scheme_and_banded()
    {
        // quiz 18.5/20 (w1), midterm 15/20 (w2): (0.925 + 2*0.75)/3 = 0.808333 → 16.17 on 0–20
        var total = GradeCalculator.Total([new(1, 20, 18.5m, ResultMark.Scored), new(2, 20, 15m, ResultMark.Scored)], Ir20)!;
        Assert.Equal(16.17m, total.Value);
        Assert.Equal("good", total.Label);
        Assert.True(total.Passed);
    }

    [Fact]
    public void Different_max_scores_are_normalized_before_weighting()
    {
        var total = GradeCalculator.Total([new(1, 10, 5m, ResultMark.Scored), new(1, 100, 100m, ResultMark.Scored)], Ir20)!;
        Assert.Equal(15m, total.Value);
    }

    [Fact]
    public void Exempt_is_excluded_and_absent_or_missing_count_as_zero_by_default()
    {
        var exempt = GradeCalculator.Total([new(1, 20, 20m, ResultMark.Scored), new(3, 20, null, ResultMark.Exempt)], Ir20)!;
        Assert.Equal(20m, exempt.Value);
        var absent = GradeCalculator.Total([new(1, 20, 20m, ResultMark.Scored), new(1, 20, null, ResultMark.Absent)], Ir20)!;
        Assert.Equal(10m, absent.Value);
        var unmarked = GradeCalculator.Total([new(1, 20, 20m, ResultMark.Scored), new(1, 20, null, null)], Ir20)!;
        Assert.Equal(10m, unmarked.Value);
        var lenient = Ir20 with { MissingCountsAsZero = false };
        Assert.Equal(20m, GradeCalculator.Total([new(1, 20, 20m, ResultMark.Scored), new(1, 20, null, ResultMark.Missing)], lenient)!.Value);
    }

    [Fact]
    public void No_counted_weight_gives_no_total()
    {
        Assert.Null(GradeCalculator.Total([], Ir20));
        Assert.Null(GradeCalculator.Total([new(1, 20, null, ResultMark.Exempt)], Ir20));
    }

    [Fact]
    public void Rounding_is_half_away_from_zero_and_pass_threshold_is_inclusive()
    {
        // 0.4975 * 20 = 9.95 → 9.95 (2 dp) ; pass is 10
        var below = GradeCalculator.FromNormalized(0.4975m, Ir20);
        Assert.Equal(9.95m, below.Value);
        Assert.False(below.Passed);
        Assert.Equal("fail", below.Label);
        var atPass = GradeCalculator.FromNormalized(0.5m, Ir20);
        Assert.True(atPass.Passed);
        Assert.Equal("pass", atPass.Label);
        var zeroDp = GradeCalculator.FromNormalized(0.4975m, Ir20 with { DecimalPlaces = 0 });
        Assert.Equal(10m, zeroDp.Value);
    }

    [Fact]
    public void Letter_scheme_uses_bands_on_a_100_scale()
    {
        Assert.Equal("A", GradeCalculator.FromNormalized(0.95m, Letter).Label);
        Assert.Equal("B", GradeCalculator.FromNormalized(0.80m, Letter).Label);
        var f = GradeCalculator.FromNormalized(0.59m, Letter);
        Assert.Equal("F", f.Label);
        Assert.False(f.Passed);
    }

    [Fact]
    public void Default_schemes_make_IR20_the_only_default()
    {
        var schemes = DefaultGradingSchemes.Create();
        Assert.Equal([DefaultGradingSchemes.Iranian20], schemes.Where(s => s.IsDefault).Select(s => s.Code).ToArray());
        var ir20 = schemes.Single(s => s.IsDefault);
        Assert.Equal((0m, 20m, 10m), (ir20.MinScore, ir20.MaxScore, ir20.PassScore));
        Assert.All(schemes.SelectMany(s => s.Bands), b => Assert.NotEqual(Guid.Empty, b.GradingSchemeId));
    }
}

public class AssessmentWorkflowTests
{
    private static Assessment WithScore()
    {
        var a = new Assessment { Weight = 1, MaxScore = 20 };
        a.Results.Add(new AssessmentResult { StudentId = Guid.NewGuid(), Mark = ResultMark.Scored, Score = 12 });
        return a;
    }

    [Fact]
    public void Happy_path_draft_submitted_approved_published_copies_snapshot()
    {
        var a = WithScore();
        var now = DateTime.UtcNow;
        AssessmentWorkflow.Apply(a, GradeAuditAction.Submitted, now, null);
        Assert.Equal(AssessmentStatus.Submitted, a.Status);
        AssessmentWorkflow.Apply(a, GradeAuditAction.Approved, now, null);
        Assert.False(a.Results[0].IsPublished);
        AssessmentWorkflow.Apply(a, GradeAuditAction.Published, now, null);
        Assert.Equal(AssessmentStatus.Published, a.Status);
        Assert.True(a.Results[0].IsPublished);
        Assert.Equal(12m, a.Results[0].PublishedScore);
        Assert.Equal(20m, a.PublishedMaxScore);
    }

    [Fact]
    public void Reopen_keeps_last_published_snapshot_until_republished()
    {
        var a = WithScore();
        var now = DateTime.UtcNow;
        foreach (var step in new[] { GradeAuditAction.Submitted, GradeAuditAction.Approved, GradeAuditAction.Published, GradeAuditAction.Reopened })
            AssessmentWorkflow.Apply(a, step, now, "fix");
        Assert.Equal(AssessmentStatus.Draft, a.Status);
        a.Results[0].Score = 14;
        Assert.Equal(12m, a.Results[0].PublishedScore);
        Assert.True(a.EverPublished);
        foreach (var step in new[] { GradeAuditAction.Submitted, GradeAuditAction.Approved, GradeAuditAction.Published })
            AssessmentWorkflow.Apply(a, step, now, null);
        Assert.Equal(14m, a.Results[0].PublishedScore);
    }

    [Fact]
    public void Return_sends_submitted_back_to_draft_with_reason()
    {
        var a = WithScore();
        AssessmentWorkflow.Apply(a, GradeAuditAction.Submitted, DateTime.UtcNow, null);
        AssessmentWorkflow.Apply(a, GradeAuditAction.Returned, DateTime.UtcNow, "check Q3");
        Assert.Equal(AssessmentStatus.Draft, a.Status);
        Assert.Equal("check Q3", a.ReturnReason);
        Assert.True(AssessmentWorkflow.RequiresReason(GradeAuditAction.Returned));
        Assert.True(AssessmentWorkflow.RequiresReason(GradeAuditAction.Reopened));
        Assert.False(AssessmentWorkflow.RequiresReason(GradeAuditAction.Approved));
    }

    [Theory]
    [InlineData(AssessmentStatus.Draft, GradeAuditAction.Approved)]
    [InlineData(AssessmentStatus.Draft, GradeAuditAction.Published)]
    [InlineData(AssessmentStatus.Submitted, GradeAuditAction.Published)]
    [InlineData(AssessmentStatus.Approved, GradeAuditAction.Submitted)]
    [InlineData(AssessmentStatus.Published, GradeAuditAction.Approved)]
    [InlineData(AssessmentStatus.Draft, GradeAuditAction.Reopened)]
    public void Invalid_transitions_are_rejected(AssessmentStatus from, GradeAuditAction action)
    {
        var a = new Assessment { Status = from };
        var ex = Assert.Throws<ConflictException>(() => AssessmentWorkflow.Apply(a, action, DateTime.UtcNow, "r"));
        Assert.Equal(AcademicErrors.InvalidTransition, ex.Code);
        Assert.Equal(from, a.Status);
    }
}

public class ScheduleRuleTests
{
    private static WeeklySlot S(DayOfWeek d, int h1, int m1, int h2, int m2) => new(d, new TimeOnly(h1, m1), new TimeOnly(h2, m2));

    [Fact]
    public void Overlap_is_half_open_and_per_weekday()
    {
        Assert.True(ScheduleRules.Overlaps(S(DayOfWeek.Saturday, 8, 0, 9, 30), S(DayOfWeek.Saturday, 9, 0, 10, 0)));
        Assert.False(ScheduleRules.Overlaps(S(DayOfWeek.Saturday, 8, 0, 9, 0), S(DayOfWeek.Saturday, 9, 0, 10, 0)));
        Assert.False(ScheduleRules.Overlaps(S(DayOfWeek.Saturday, 8, 0, 9, 30), S(DayOfWeek.Sunday, 8, 0, 9, 30)));
        Assert.True(ScheduleRules.Overlaps(S(DayOfWeek.Monday, 8, 0, 12, 0), S(DayOfWeek.Monday, 9, 0, 10, 0)));
    }

    [Fact]
    public void Iranian_week_starts_on_saturday()
    {
        var order = Enum.GetValues<DayOfWeek>().OrderBy(ScheduleRules.WeekOrder).ToArray();
        Assert.Equal(DayOfWeek.Saturday, order[0]);
        Assert.Equal(DayOfWeek.Friday, order[^1]);
    }

    [Fact]
    public void Teacher_assignment_is_effective_dated()
    {
        var a = new TeacherAssignment { StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 12, 31) };
        Assert.False(a.IsActiveOn(new DateOnly(2026, 9, 30)));
        Assert.True(a.IsActiveOn(new DateOnly(2026, 10, 1)));
        Assert.True(a.IsActiveOn(new DateOnly(2026, 12, 31)));
        Assert.False(a.IsActiveOn(new DateOnly(2027, 1, 1)));
        Assert.True(new TeacherAssignment().IsActiveOn(DateOnly.MinValue));
    }
}

/// <summary>Enrollment rules exercised through the real service on an in-memory SQLite academic context.</summary>
#pragma warning disable CA1001 // disposed in DisposeAsync (xunit IAsyncLifetime)
public sealed class EnrollmentRuleTests : IAsyncLifetime
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly TenantContext _tenant = new();
    private readonly FakePeople _people = new();
    private readonly Guid _branch = Guid.NewGuid();
    private AcademicDbContext _db = null!;
    private EnrollmentService _service = null!;
    private Term _term = null!;

    public async Task InitializeAsync()
    {
        await _conn.OpenAsync();
        _tenant.Initialize(Guid.NewGuid(), Guid.NewGuid(), false);
        var options = new DbContextOptionsBuilder<AcademicDbContext>().UseSqlite(_conn).AddInterceptors(new RowVersionEmulation())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.SqliteEventId.SchemaConfiguredWarning)).Options;
        _db = new AcademicDbContext(options, _tenant);
        await _db.Database.EnsureCreatedAsync();
        var access = new AllowAll();
        var actor = new AcademicActor(_tenant, _people, new FixedSettings(), TimeProvider.System);
        _service = new EnrollmentService(_db, access, new ClassAccess(_db, access, actor), actor, _people);

        var year = new AcademicYear { Name = "Y", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2030, 1, 1) };
        _term = new Term { AcademicYearId = year.Id, Name = "T", StartDate = year.StartDate, EndDate = year.EndDate };
        var subject = new Subject { Code = "S", Name = "S" };
        var scheme = DefaultGradingSchemes.Create()[0];
        var course = new Course { SubjectId = subject.Id, Code = "C", Name = "C" };
        _db.AddRange(year, _term, subject, scheme, course);
        await _db.SaveChangesAsync();
        CourseId = course.Id;
        SchemeId = scheme.Id;
    }

    private Guid CourseId { get; set; }
    private Guid SchemeId { get; set; }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
    }

    private async Task<ClassSection> ClassAsync(int capacity, ClassStatus status = ClassStatus.Active, params (DayOfWeek Day, int From, int To)[] slots)
    {
        var c = new ClassSection { TermId = _term.Id, CourseId = CourseId, BranchId = _branch, Code = $"K{Guid.NewGuid():N}"[..10], Name = "K",
            Capacity = capacity, Status = status, GradingSchemeId = SchemeId };
        foreach (var (d, f, t) in slots) c.Schedule.Add(new ScheduleSlot { ClassId = c.Id, Weekday = d, StartTime = new TimeOnly(f, 0), EndTime = new TimeOnly(t, 0) });
        _db.Classes.Add(c);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return c;
    }

    private Guid Student(Guid? branch = null) => _people.AddStudent(branch ?? _branch);

    [Fact]
    public async Task Capacity_is_enforced_and_count_is_maintained()
    {
        var c = await ClassAsync(1);
        await _service.EnrollAsync(new EnrollRequest(c.Id, Student(), null), default);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.EnrollAsync(new EnrollRequest(c.Id, Student(), null), default));
        Assert.Equal(AcademicErrors.ClassFull, ex.Code);
        Assert.Equal(1, (await _db.Classes.AsNoTracking().FirstAsync(x => x.Id == c.Id)).EnrolledCount);
    }

    [Fact]
    public async Task Duplicate_active_enrollment_is_rejected_and_withdrawn_one_is_reactivated()
    {
        var c = await ClassAsync(5);
        var s = Student();
        var first = await _service.EnrollAsync(new EnrollRequest(c.Id, s, null), default);
        var dup = await Assert.ThrowsAsync<ConflictException>(() => _service.EnrollAsync(new EnrollRequest(c.Id, s, null), default));
        Assert.Equal(AcademicErrors.AlreadyEnrolled, dup.Code);
        _db.ChangeTracker.Clear();
        await _service.WithdrawAsync(first.Id, new WithdrawRequest(null, first.RowVersion), default);
        _db.ChangeTracker.Clear();
        var again = await _service.EnrollAsync(new EnrollRequest(c.Id, s, null), default);
        Assert.Equal(first.Id, again.Id);
        Assert.Equal("active", again.Status);
        Assert.Equal(1, (await _db.Classes.AsNoTracking().FirstAsync(x => x.Id == c.Id)).EnrolledCount);
    }

    [Fact]
    public async Task Closed_class_wrong_branch_and_unknown_student_are_rejected()
    {
        var closed = await ClassAsync(5, ClassStatus.Completed);
        Assert.Equal(AcademicErrors.ClassNotOpen,
            (await Assert.ThrowsAsync<BusinessRuleException>(() => _service.EnrollAsync(new EnrollRequest(closed.Id, Student(), null), default))).Code);
        var open = await ClassAsync(5);
        Assert.Equal(AcademicErrors.StudentBranchMismatch,
            (await Assert.ThrowsAsync<BusinessRuleException>(() => _service.EnrollAsync(new EnrollRequest(open.Id, Student(Guid.NewGuid()), null), default))).Code);
        var v = await Assert.ThrowsAsync<ValidationException>(() => _service.EnrollAsync(new EnrollRequest(open.Id, Guid.NewGuid(), null), default));
        Assert.Contains(AcademicErrors.StudentNotFound, v.Errors["studentId"]);
    }

    [Fact]
    public async Task Overlapping_timetable_in_same_term_is_a_conflict()
    {
        var a = await ClassAsync(5, ClassStatus.Active, (DayOfWeek.Saturday, 8, 10));
        var b = await ClassAsync(5, ClassStatus.Active, (DayOfWeek.Saturday, 9, 11));
        var c = await ClassAsync(5, ClassStatus.Active, (DayOfWeek.Saturday, 10, 12));
        var s = Student();
        await _service.EnrollAsync(new EnrollRequest(a.Id, s, null), default);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.EnrollAsync(new EnrollRequest(b.Id, s, null), default));
        Assert.Equal(AcademicErrors.ScheduleConflict, ex.Code);
        Assert.Equal(a.Id, ex.Parameters["classId"]);
        _db.ChangeTracker.Clear();
        await _service.EnrollAsync(new EnrollRequest(c.Id, s, null), default); // back-to-back is fine
    }

    /// <summary>Stands in for the production persistence interceptor's SQLite rowversion emulation.</summary>
    private sealed class RowVersionEmulation : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            foreach (var e in eventData.Context!.ChangeTracker.Entries<EEMP.SharedKernel.Domain.IHasRowVersion>()
                         .Where(e => e.State is EntityState.Added or EntityState.Modified))
                e.Entity.RowVersion = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class AllowAll : IAccessControl
    {
        public Task<bool> HasAnyAsync(string permission, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> HasTenantWideAsync(string permission, CancellationToken ct = default) => Task.FromResult(true);
        public Task<BranchScope> GetBranchScopeAsync(string permission, CancellationToken ct = default) => Task.FromResult(BranchScope.Tenant);
        public Task EnsureBranchAsync(string permission, Guid branchId, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureTenantWideAsync(string permission, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FixedSettings : IOrganizationSettings
    {
        public Task<string> GetTimeZoneAsync(CancellationToken ct = default) => Task.FromResult("Asia/Tehran");
        public Task<string> GetInstituteNameAsync(CancellationToken ct = default) => Task.FromResult("Test institute");
    }

    private sealed class FakePeople : IPeopleDirectory
    {
        private readonly Dictionary<Guid, StudentSummary> _students = [];

        public Guid AddStudent(Guid branch)
        {
            var id = Guid.NewGuid();
            _students[id] = new StudentSummary(id, branch, "F", "L", id.ToString("N")[..6], true);
            return id;
        }

        public Task<IReadOnlyDictionary<Guid, StudentSummary>> GetStudentsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, StudentSummary>>(_students.Where(s => ids.Contains(s.Key)).ToDictionary());
        public Task<IReadOnlyDictionary<Guid, TeacherSummary>> GetTeachersAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, TeacherSummary>>(new Dictionary<Guid, TeacherSummary>());
        public Task<Guid?> FindTeacherIdForUserAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<Guid?>(null);
        public Task<Guid?> FindStudentIdForUserAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<Guid?>(null);
        public Task<IReadOnlyList<Guid>> GetSiblingIdsAsync(Guid studentId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<IReadOnlyList<Guid>> GetGuardianStudentIdsForUserAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
    }
}
