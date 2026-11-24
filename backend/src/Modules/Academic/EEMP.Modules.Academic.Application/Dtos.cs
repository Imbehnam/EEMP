namespace EEMP.Modules.Academic.Application;

// Dates are yyyy-MM-dd (tenant-local calendar dates), times HH:mm, enums camelCase strings, rowVersion base64.

public sealed record AcademicYearDto(Guid Id, string Name, string StartDate, string EndDate, string Status, string RowVersion);
public sealed record AcademicYearRequest(string? Name, string? StartDate, string? EndDate, string? Status, string? RowVersion);

public sealed record TermDto(Guid Id, Guid AcademicYearId, string Name, string StartDate, string EndDate, int SortOrder, string RowVersion);
public sealed record TermRequest(Guid? AcademicYearId, string? Name, string? StartDate, string? EndDate, int? SortOrder, string? RowVersion);

public sealed record SubjectDto(Guid Id, string Code, string Name, string? Description, string RowVersion);
public sealed record SubjectRequest(string? Code, string? Name, string? Description, string? RowVersion);

public sealed record CourseDto(Guid Id, Guid SubjectId, string Code, string Name, string? GradeLevel, string? Description, Guid? GradingSchemeId, string RowVersion);
public sealed record CourseRequest(Guid? SubjectId, string? Code, string? Name, string? GradeLevel, string? Description, Guid? GradingSchemeId, string? RowVersion);

public sealed record GradeBandDto(string Label, decimal MinValue);
public sealed record GradingSchemeDto(Guid Id, string Code, string Name, string Kind, decimal MinScore, decimal MaxScore, decimal PassScore,
    int DecimalPlaces, bool MissingCountsAsZero, bool IsDefault, IReadOnlyList<GradeBandDto> Bands, string RowVersion);
public sealed record GradingSchemeRequest(string? Code, string? Name, string? Kind, decimal? MinScore, decimal? MaxScore, decimal? PassScore,
    int? DecimalPlaces, bool? MissingCountsAsZero, bool? IsDefault, IReadOnlyList<GradeBandDto>? Bands, string? RowVersion);
public sealed record SchemeInfoDto(string Code, string Name, string Kind, decimal MinScore, decimal MaxScore, decimal PassScore, int DecimalPlaces);

public sealed record ScheduleSlotDto(Guid Id, string Weekday, string StartTime, string EndTime, string? Room);
public sealed record TeacherAssignmentDto(Guid Id, Guid TeacherId, string FirstName, string LastName, string Role, string? StartDate, string? EndDate, bool IsActive);
public sealed record ClassDto(Guid Id, Guid TermId, Guid AcademicYearId, Guid CourseId, Guid SubjectId, Guid BranchId, string Code, string Name,
    int Capacity, int EnrolledCount, string Status, Guid GradingSchemeId, string? DefaultRoom, IReadOnlyList<ScheduleSlotDto> Schedule,
    IReadOnlyList<TeacherAssignmentDto> Teachers, string RowVersion);
public sealed record ClassRequest(Guid? TermId, Guid? CourseId, Guid? BranchId, string? Code, string? Name, int? Capacity, string? Status,
    Guid? GradingSchemeId, string? DefaultRoom, string? RowVersion);
public sealed record ScheduleSlotRequest(string? Weekday, string? StartTime, string? EndTime, string? Room);
public sealed record ScheduleRequest(IReadOnlyList<ScheduleSlotRequest>? Slots, string? RowVersion);
public sealed record TeacherAssignmentRequest(Guid? TeacherId, string? Role, string? StartDate, string? EndDate);
public sealed record TeachersRequest(IReadOnlyList<TeacherAssignmentRequest>? Assignments, string? RowVersion);
public sealed record RosterEntryDto(Guid EnrollmentId, Guid StudentId, string FirstName, string LastName, string StudentNumber, string Status, string EnrolledOn);

public sealed record EnrollmentDto(Guid Id, Guid ClassId, Guid StudentId, string FirstName, string LastName, string StudentNumber, string Status,
    string EnrolledOn, string? WithdrawnOn, string RowVersion);
public sealed record EnrollRequest(Guid? ClassId, Guid? StudentId, string? EnrolledOn);
public sealed record WithdrawRequest(string? WithdrawnOn, string? RowVersion);

public sealed record AttendanceSummaryDto(int Present, int Absent, int Late, int Excused, int Unmarked);
public sealed record AttendanceEntryDto(Guid StudentId, string FirstName, string LastName, string StudentNumber, string? Status, string? Note);
public sealed record AttendanceSheetDto(Guid? SessionId, Guid ClassId, string Date, Guid? ScheduleSlotId, Guid? TakenByUserId, DateTime? UpdatedAt,
    string? RowVersion, AttendanceSummaryDto Summary, IReadOnlyList<AttendanceEntryDto> Entries);
public sealed record AttendanceMarkRequest(Guid? StudentId, string? Status, string? Note);
public sealed record AttendanceBulkRequest(string? Date, Guid? ScheduleSlotId, string? RowVersion, IReadOnlyList<AttendanceMarkRequest>? Records);
public sealed record AttendanceSessionDto(Guid Id, string Date, Guid? ScheduleSlotId, int Present, int Absent, int Late, int Excused, string RowVersion);
public sealed record AttendanceRecordDto(Guid Id, Guid SessionId, Guid ClassId, Guid StudentId, string Date, Guid? ScheduleSlotId, string Status, string? Note);

public sealed record AssessmentDto(Guid Id, Guid ClassId, string Name, string Type, decimal Weight, decimal MaxScore, string? Date, string Status,
    DateTime? SubmittedAt, DateTime? ApprovedAt, DateTime? PublishedAt, string? ReturnReason, string RowVersion);
public sealed record AssessmentRequest(string? Name, string? Type, decimal? Weight, decimal? MaxScore, string? Date, string? RowVersion);
public sealed record ResultRowDto(Guid StudentId, string FirstName, string LastName, string StudentNumber, decimal? Score, string? Mark, string? Comment);
public sealed record ResultsDto(AssessmentDto Assessment, IReadOnlyList<ResultRowDto> Results);
public sealed record ResultEntryRequest(Guid? StudentId, string? Mark, decimal? Score, string? Comment, string? Reason);
public sealed record ResultsRequest(string? RowVersion, IReadOnlyList<ResultEntryRequest>? Results);
public sealed record TransitionRequest(string? RowVersion, string? Reason);
public sealed record GradeHistoryDto(string Action, Guid? StudentId, decimal? OldScore, decimal? NewScore, string? OldMark, string? NewMark,
    string? Reason, Guid? ActorUserId, DateTime OccurredAt);

public sealed record GradeTotalDto(decimal Value, string? Label, bool Passed);
public sealed record GradebookScoreDto(Guid AssessmentId, decimal? Score, string? Mark);
public sealed record GradebookStudentDto(Guid StudentId, string FirstName, string LastName, string StudentNumber, IReadOnlyList<GradebookScoreDto> Scores, GradeTotalDto? Total);
public sealed record GradebookDto(Guid ClassId, SchemeInfoDto GradingScheme, IReadOnlyList<AssessmentDto> Assessments, IReadOnlyList<GradebookStudentDto> Students);

public sealed record GenerateReportCardsRequest(Guid? TermId, Guid? ClassId, Guid? BranchId);
public sealed record PublishReportCardsRequest(Guid? TermId, Guid? ClassId, Guid? BranchId);
public sealed record ReportCardSummaryDto(Guid Id, Guid StudentId, string FirstName, string LastName, string StudentNumber, Guid TermId, Guid BranchId,
    string Status, int CurrentVersion, int? PublishedVersion, DateTime GeneratedAt, DateTime? PublishedAt, GradeTotalDto? Average, string RowVersion);
public sealed record GenerateReportCardsResult(int Created, int Regenerated, int Unchanged, IReadOnlyList<ReportCardSummaryDto> Items);
public sealed record ReportCardVersionInfoDto(int VersionNo, string Status, DateTime GeneratedAt, DateTime? PublishedAt);
public sealed record ReportCardDto(Guid Id, Guid StudentId, string FirstName, string LastName, string StudentNumber, Guid TermId, Guid BranchId,
    string Status, int CurrentVersion, int? PublishedVersion, DateTime GeneratedAt, DateTime? PublishedAt, GradeTotalDto? Average, string RowVersion,
    IReadOnlyList<ReportCardVersionInfoDto> Versions, ReportCardContent Content);
public sealed record ReportCardVersionDto(int VersionNo, string Status, DateTime GeneratedAt, DateTime? PublishedAt, ReportCardContent Content);
public sealed record PublishCountDto(int Published);

// Report card snapshot (serialized into ReportCardVersion.Content; immutable once published).
public sealed record RcStudent(Guid Id, string FirstName, string LastName, string StudentNumber);
public sealed record RcRef(Guid Id, string Name);
public sealed record RcTerm(Guid Id, string Name, string StartDate, string EndDate);
public sealed record RcAssessment(string Name, string Type, decimal Weight, decimal MaxScore, decimal? Score, string? Mark);
public sealed record RcLine(Guid ClassId, string ClassCode, string ClassName, string CourseName, string SubjectName, SchemeInfoDto GradingScheme,
    GradeTotalDto? Total, IReadOnlyList<RcAssessment> Assessments);
public sealed record RcAttendance(int Present, int Absent, int Late, int Excused);
public sealed record ReportCardContent(string Formula, DateTime GeneratedAt, RcStudent Student, RcRef AcademicYear, RcTerm Term, SchemeInfoDto GradingScheme,
    IReadOnlyList<RcLine> Lines, GradeTotalDto? Average, RcAttendance Attendance);

// Portal
public sealed record TeacherClassDto(Guid Id, string Code, string Name, string CourseName, string SubjectName, Guid TermId, string TermName, Guid BranchId,
    string Role, int EnrolledCount, string Status, IReadOnlyList<ScheduleSlotDto> Schedule);
public sealed record PersonNameDto(string FirstName, string LastName);
public sealed record TimetableEntryDto(Guid ClassId, string ClassCode, string ClassName, string CourseName, string SubjectName, string Weekday,
    string StartTime, string EndTime, string? Room, IReadOnlyList<PersonNameDto> Teachers);
public sealed record PortalAttendanceRecordDto(string Date, Guid ClassId, string ClassName, Guid? ScheduleSlotId, string Status, string? Note);
public sealed record PortalAttendanceDto(RcAttendance Summary, IReadOnlyList<PortalAttendanceRecordDto> Records);
public sealed record PortalAssessmentDto(Guid Id, string Name, string Type, decimal Weight, decimal MaxScore, decimal? Score, string? Mark, DateTime? PublishedAt);
public sealed record PortalClassGradesDto(Guid ClassId, string ClassName, string CourseName, string SubjectName, Guid TermId, SchemeInfoDto GradingScheme,
    IReadOnlyList<PortalAssessmentDto> Assessments, GradeTotalDto? Total);
public sealed record PortalReportCardSummaryDto(Guid Id, Guid TermId, string TermName, int VersionNo, DateTime? PublishedAt);
public sealed record PortalReportCardDto(Guid Id, Guid StudentId, Guid TermId, int VersionNo, DateTime? PublishedAt, ReportCardContent Content);
