using EEMP.SharedKernel.Domain;

namespace EEMP.Modules.Academic.Domain;

public enum AcademicYearStatus { Planned, Active, Closed }

public enum GradingSchemeKind { Numeric, Percentage, Letter }

public enum ClassStatus { Planned, Active, Completed, Cancelled }

public enum TeacherRole { Primary, Assistant, Substitute }

public enum EnrollmentStatus { Active, Withdrawn, Completed }

public enum AttendanceStatus { Present, Absent, Late, Excused }

public enum AssessmentType { Exam, Quiz, Assignment, Project, Participation, Midterm, Final, Other }

public enum AssessmentStatus { Draft, Submitted, Approved, Published }

public enum ResultMark { Scored, Absent, Exempt, Missing }

public enum GradeAuditAction { ScoreChanged, Submitted, Returned, Approved, Published, Reopened }

public enum ReportCardStatus { Draft, Published }

public enum ReportCardVersionStatus { Draft, Published, Superseded }

/// <summary>Academic year, e.g. "1405-1406". Dates are tenant-local calendar dates (Gregorian ISO; Jalali is display only).</summary>
public sealed class AcademicYear : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public AcademicYearStatus Status { get; set; } = AcademicYearStatus.Planned;
}

public sealed class Term : TenantEntity
{
    public Guid AcademicYearId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int SortOrder { get; set; }

    public bool Contains(DateOnly date) => date >= StartDate && date <= EndDate;
}

public sealed class Subject : TenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>Reusable course template (class definition). A <see cref="ClassSection"/> is its offering in a term and branch.</summary>
public sealed class Course : TenantEntity
{
    public Guid SubjectId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? GradeLevel { get; set; }
    public string? Description { get; set; }
    public Guid? GradingSchemeId { get; set; }
}

/// <summary>Configurable grading scheme (OD-007 accepted assumption: numeric 0–20 default, percentage and letter per tenant).</summary>
public sealed class GradingScheme : TenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public GradingSchemeKind Kind { get; set; }
    public decimal MinScore { get; set; }
    public decimal MaxScore { get; set; }
    public decimal PassScore { get; set; }
    public int DecimalPlaces { get; set; } = 2;
    public bool MissingCountsAsZero { get; set; } = true;
    public bool IsDefault { get; set; }
    public List<GradeBand> Bands { get; set; } = [];
}

public sealed class GradeBand : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid GradingSchemeId { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal MinValue { get; set; }
}

/// <summary>A class/section: offering of a course in a term at one branch, with capacity, schedule and teachers.</summary>
public sealed class ClassSection : TenantEntity
{
    public Guid TermId { get; set; }
    public Guid CourseId { get; set; }
    public Guid BranchId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Capacity { get; set; }
    /// <summary>Active enrollments. Maintained with the class rowversion so concurrent enrollments cannot exceed capacity.</summary>
    public int EnrolledCount { get; set; }
    public ClassStatus Status { get; set; } = ClassStatus.Planned;
    public Guid GradingSchemeId { get; set; }
    public string? DefaultRoom { get; set; }
    public List<ScheduleSlot> Schedule { get; set; } = [];
    public List<TeacherAssignment> Teachers { get; set; } = [];

    public bool IsOpenForEnrollment => Status is ClassStatus.Planned or ClassStatus.Active;
}

public sealed class ScheduleSlot : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ClassId { get; set; }
    public DayOfWeek Weekday { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public string? Room { get; set; }
}

/// <summary>Effective-dated teacher ↔ class assignment. Teacher access to a class requires an active assignment.</summary>
public sealed class TeacherAssignment : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ClassId { get; set; }
    public Guid TeacherId { get; set; }
    public TeacherRole Role { get; set; } = TeacherRole.Primary;
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public bool IsActiveOn(DateOnly date) => (StartDate is null || StartDate <= date) && (EndDate is null || EndDate >= date);
}

public sealed class Enrollment : TenantEntity
{
    public Guid ClassId { get; set; }
    public Guid StudentId { get; set; }
    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Active;
    public DateOnly EnrolledOn { get; set; }
    public DateOnly? WithdrawnOn { get; set; }
}

/// <summary>One attendance sheet per class, date and schedule slot (Guid.Empty = whole day).</summary>
public sealed class AttendanceSession : TenantEntity
{
    public Guid ClassId { get; set; }
    public DateOnly Date { get; set; }
    public Guid ScheduleSlotId { get; set; }
    public Guid? TakenByUserId { get; set; }
    public List<AttendanceRecord> Records { get; set; } = [];
}

public sealed class AttendanceRecord : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SessionId { get; set; }
    public Guid StudentId { get; set; }
    public AttendanceStatus Status { get; set; }
    public string? Note { get; set; }
    public Guid? MarkedByUserId { get; set; }
    public DateTime MarkedAt { get; set; }
}

public sealed class Assessment : TenantEntity
{
    public Guid ClassId { get; set; }
    public string Name { get; set; } = string.Empty;
    public AssessmentType Type { get; set; }
    public decimal Weight { get; set; }
    public decimal MaxScore { get; set; }
    public DateOnly? Date { get; set; }
    public AssessmentStatus Status { get; set; } = AssessmentStatus.Draft;
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? ReturnReason { get; set; }
    /// <summary>Weight/max score as last published (the portal reads the published snapshot only).</summary>
    public decimal? PublishedWeight { get; set; }
    public decimal? PublishedMaxScore { get; set; }
    public List<AssessmentResult> Results { get; set; } = [];

    public bool EverPublished => PublishedAt is not null;
}

/// <summary>Score of one student. Published* fields are the snapshot visible to students and guardians.</summary>
public sealed class AssessmentResult : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid AssessmentId { get; set; }
    public Guid StudentId { get; set; }
    public decimal? Score { get; set; }
    public ResultMark Mark { get; set; }
    public string? Comment { get; set; }
    public bool IsPublished { get; set; }
    public decimal? PublishedScore { get; set; }
    public ResultMark? PublishedMark { get; set; }
    public string? PublishedComment { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Append-only grade history: every score change and workflow transition, with old/new values and actor.</summary>
[NotAudited]
public sealed class GradeAuditEntry : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid AssessmentId { get; set; }
    public Guid? StudentId { get; set; }
    public GradeAuditAction Action { get; set; }
    public decimal? OldScore { get; set; }
    public decimal? NewScore { get; set; }
    public ResultMark? OldMark { get; set; }
    public ResultMark? NewMark { get; set; }
    public string? Reason { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTime OccurredAt { get; set; }
}

/// <summary>Report card per student and term. Content lives in immutable versions; corrections create new versions.</summary>
public sealed class ReportCard : TenantEntity
{
    public Guid StudentId { get; set; }
    public Guid TermId { get; set; }
    public Guid BranchId { get; set; }
    public ReportCardStatus Status { get; set; } = ReportCardStatus.Draft;
    public int CurrentVersion { get; set; }
    public int? PublishedVersion { get; set; }
    public DateTime? PublishedAt { get; set; }
    public decimal? AverageValue { get; set; }
    public List<ReportCardVersion> Versions { get; set; } = [];
}

public sealed class ReportCardVersion : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ReportCardId { get; set; }
    public int VersionNo { get; set; }
    public ReportCardVersionStatus Status { get; set; } = ReportCardVersionStatus.Draft;
    public string Content { get; set; } = "{}";
    public DateTime GeneratedAt { get; set; }
    public Guid? GeneratedByUserId { get; set; }
    public DateTime? PublishedAt { get; set; }
    public Guid? PublishedByUserId { get; set; }
}
