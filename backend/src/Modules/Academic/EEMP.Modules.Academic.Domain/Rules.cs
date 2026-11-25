using EEMP.SharedKernel.Errors;

namespace EEMP.Modules.Academic.Domain;

/// <summary>Stable problem/field codes of the academic module (translated in the server catalogs fa/en/tr/ar).</summary>
public static class AcademicErrors
{
    public const string YearInUse = "academic.year_in_use";
    public const string TermOutsideYear = "academic.term_outside_year";
    public const string TermOverlap = "academic.term_overlap";
    public const string TermInUse = "academic.term_in_use";
    public const string SubjectInUse = "academic.subject_in_use";
    public const string CourseInUse = "academic.course_in_use";
    public const string GradingSchemeInUse = "academic.grading_scheme_in_use";
    public const string ClassInUse = "academic.class_in_use";
    public const string ClassNotOpen = "academic.class_not_open";
    public const string ClassFull = "academic.class_full";
    public const string AlreadyEnrolled = "academic.already_enrolled";
    public const string ScheduleConflict = "academic.schedule_conflict";
    public const string RoomConflict = "academic.room_conflict";
    public const string TeacherConflict = "academic.teacher_conflict";
    public const string StudentBranchMismatch = "academic.student_branch_mismatch";
    public const string TeacherBranchMismatch = "academic.teacher_branch_mismatch";
    public const string CapacityBelowEnrollment = "academic.capacity_below_enrollment";
    public const string StudentNotEnrolled = "academic.student_not_enrolled";
    public const string AttendanceFutureDate = "academic.attendance_future_date";
    public const string DateOutsideTerm = "academic.date_outside_term";
    public const string SlotWeekdayMismatch = "academic.slot_weekday_mismatch";
    public const string InvalidTransition = "academic.invalid_transition";
    public const string AssessmentLocked = "academic.assessment_locked";
    public const string ResultsIncomplete = "academic.results_incomplete";
    public const string PersonInUse = "people.in_use";

    // Field-level codes (validation errors[field]).
    public const string TermNotFound = "academic.term_not_found";
    public const string YearNotFound = "academic.year_not_found";
    public const string SubjectNotFound = "academic.subject_not_found";
    public const string CourseNotFound = "academic.course_not_found";
    public const string GradingSchemeNotFound = "academic.grading_scheme_not_found";
    public const string BranchNotFound = "academic.branch_not_found";
    public const string StudentNotFound = "academic.student_not_found";
    public const string TeacherNotFound = "academic.teacher_not_found";
    public const string SlotNotFound = "academic.slot_not_found";
    public const string InvalidDateRange = "academic.invalid_date_range";
    public const string InvalidTimeRange = "academic.invalid_time_range";
    public const string SlotOverlap = "academic.slot_overlap";
    public const string ScoreOutOfRange = "academic.score_out_of_range";
}

/// <summary>Grade-entry workflow: draft → submitted → approved → published, plus return (submitted → draft) and reopen (published → draft).</summary>
public static class AssessmentWorkflow
{
    public static readonly IReadOnlyDictionary<GradeAuditAction, (AssessmentStatus From, AssessmentStatus To)> Transitions =
        new Dictionary<GradeAuditAction, (AssessmentStatus, AssessmentStatus)>
        {
            [GradeAuditAction.Submitted] = (AssessmentStatus.Draft, AssessmentStatus.Submitted),
            [GradeAuditAction.Returned] = (AssessmentStatus.Submitted, AssessmentStatus.Draft),
            [GradeAuditAction.Approved] = (AssessmentStatus.Submitted, AssessmentStatus.Approved),
            [GradeAuditAction.Published] = (AssessmentStatus.Approved, AssessmentStatus.Published),
            [GradeAuditAction.Reopened] = (AssessmentStatus.Published, AssessmentStatus.Draft),
        };

    public static bool RequiresReason(GradeAuditAction action) => action is GradeAuditAction.Returned or GradeAuditAction.Reopened;

    /// <summary>Applies a transition or throws 409 academic.invalid_transition. Publishing copies scores to the published snapshot.</summary>
    public static void Apply(Assessment a, GradeAuditAction action, DateTime now, string? reason)
    {
        if (!Transitions.TryGetValue(action, out var t) || a.Status != t.From)
            throw new ConflictException(AcademicErrors.InvalidTransition, new Dictionary<string, object?>
            {
                ["from"] = Camel(a.Status.ToString()),
                ["to"] = Transitions.TryGetValue(action, out var tt) ? Camel(tt.To.ToString()) : null,
            });
        a.Status = t.To;
        switch (action)
        {
            case GradeAuditAction.Submitted:
                a.SubmittedAt = now;
                a.ReturnReason = null;
                break;
            case GradeAuditAction.Returned:
                a.ReturnReason = reason;
                a.SubmittedAt = null;
                break;
            case GradeAuditAction.Approved:
                a.ApprovedAt = now;
                break;
            case GradeAuditAction.Published:
                a.PublishedAt = now;
                a.PublishedWeight = a.Weight;
                a.PublishedMaxScore = a.MaxScore;
                foreach (var r in a.Results)
                {
                    r.IsPublished = true;
                    r.PublishedScore = r.Score;
                    r.PublishedMark = r.Mark;
                    r.PublishedComment = r.Comment;
                }
                break;
            case GradeAuditAction.Reopened:
                // The last published snapshot stays visible in the portal until the next publish.
                a.ReturnReason = reason;
                a.SubmittedAt = null;
                a.ApprovedAt = null;
                break;
        }
    }

    private static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];
}

public sealed record WeeklySlot(DayOfWeek Weekday, TimeOnly Start, TimeOnly End);

public static class ScheduleRules
{
    /// <summary>Half-open overlap: 08:00–09:00 and 09:00–10:00 do not overlap.</summary>
    public static bool Overlaps(WeeklySlot a, WeeklySlot b) => a.Weekday == b.Weekday && a.Start < b.End && b.Start < a.End;

    public static bool AnyOverlap(IEnumerable<WeeklySlot> a, IEnumerable<WeeklySlot> b)
    {
        var right = b.ToList();
        return a.Any(x => right.Any(y => Overlaps(x, y)));
    }

    /// <summary>Iranian week order (Saturday first) for timetables.</summary>
    public static int WeekOrder(DayOfWeek d) => ((int)d + 1) % 7;
}

public sealed record SchemeSpec(decimal MinScore, decimal MaxScore, decimal PassScore, int DecimalPlaces, bool MissingCountsAsZero,
    IReadOnlyList<(string Label, decimal MinValue)> Bands);

public sealed record ScoreInput(decimal Weight, decimal MaxScore, decimal? Score, ResultMark? Mark);

public sealed record GradeTotal(decimal Value, string? Label, bool Passed, decimal Normalized);

/// <summary>Formula weightedAverage.v1 (see contract §4): weighted mean of score/maxScore, scaled to the scheme and rounded.</summary>
public static class GradeCalculator
{
    public const string Formula = "weightedAverage.v1";

    public static decimal? Normalized(IEnumerable<ScoreInput> inputs, bool missingCountsAsZero)
    {
        decimal weighted = 0, weights = 0;
        foreach (var i in inputs)
        {
            if (i.Weight <= 0 || i.MaxScore <= 0) continue;
            switch (i.Mark)
            {
                case ResultMark.Exempt:
                    continue;
                case ResultMark.Scored when i.Score is { } s:
                    weighted += i.Weight * Math.Clamp(s / i.MaxScore, 0m, 1m);
                    weights += i.Weight;
                    break;
                default: // absent, missing, or not marked
                    if (!missingCountsAsZero) continue;
                    weights += i.Weight;
                    break;
            }
        }
        return weights == 0 ? null : weighted / weights;
    }

    public static GradeTotal? Total(IEnumerable<ScoreInput> inputs, SchemeSpec scheme)
    {
        var n = Normalized(inputs, scheme.MissingCountsAsZero);
        return n is null ? null : FromNormalized(n.Value, scheme);
    }

    public static GradeTotal FromNormalized(decimal normalized, SchemeSpec scheme)
    {
        var value = Math.Round(scheme.MinScore + normalized * (scheme.MaxScore - scheme.MinScore), scheme.DecimalPlaces, MidpointRounding.AwayFromZero);
        var label = scheme.Bands.Where(b => b.MinValue <= value).OrderByDescending(b => b.MinValue).Select(b => b.Label).FirstOrDefault();
        return new GradeTotal(value, label, value >= scheme.PassScore, normalized);
    }

    public static SchemeSpec Spec(GradingScheme s) =>
        new(s.MinScore, s.MaxScore, s.PassScore, s.DecimalPlaces, s.MissingCountsAsZero, s.Bands.Select(b => (b.Label, b.MinValue)).ToList());
}

/// <summary>Built-in schemes created for every tenant on first use (OD-007 accepted assumption).</summary>
public static class DefaultGradingSchemes
{
    public const string Iranian20 = "IR20";
    public const string Percentage = "PCT";
    public const string Letter = "LETTER";

    public static IReadOnlyList<GradingScheme> Create() =>
    [
        Scheme(Iranian20, "Numeric 0–20", GradingSchemeKind.Numeric, 0, 20, 10, true,
            [("excellent", 17), ("good", 14), ("fair", 12), ("pass", 10), ("fail", 0)]),
        Scheme(Percentage, "Percentage 0–100", GradingSchemeKind.Percentage, 0, 100, 50, false, []),
        Scheme(Letter, "Letter A–F", GradingSchemeKind.Letter, 0, 100, 60, false, [("A", 90), ("B", 80), ("C", 70), ("D", 60), ("F", 0)]),
    ];

    private static GradingScheme Scheme(string code, string name, GradingSchemeKind kind, decimal min, decimal max, decimal pass, bool isDefault,
        (string Label, decimal Min)[] bands)
    {
        var s = new GradingScheme { Code = code, Name = name, Kind = kind, MinScore = min, MaxScore = max, PassScore = pass, DecimalPlaces = 2, IsDefault = isDefault };
        s.Bands = bands.Select(b => new GradeBand { GradingSchemeId = s.Id, Label = b.Label, MinValue = b.Min }).ToList();
        return s;
    }
}
