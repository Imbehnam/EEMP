namespace EEMP.Modules.Academic.Contracts;

/// <summary>Lets other modules know whether academic records reference a branch or a person (deletion guards).</summary>
public interface IAcademicUsage
{
    Task<bool> IsBranchReferencedAsync(Guid branchId, CancellationToken ct = default);
    Task<bool> IsStudentReferencedAsync(Guid studentId, CancellationToken ct = default);
    Task<bool> IsTeacherReferencedAsync(Guid teacherId, CancellationToken ct = default);
}

public sealed record EnrollmentInfo(Guid Id, Guid StudentId, Guid ClassId, Guid BranchId, Guid TermId, Guid CourseId, bool IsActive);

public sealed record ClassInfo(Guid Id, Guid BranchId, Guid TermId, Guid CourseId, string Code, string Name);

/// <summary>Read-only enrollment lookups for other modules (finance invoice generation). No permission checks: callers authorize.</summary>
public interface IEnrollmentDirectory
{
    Task<EnrollmentInfo?> GetAsync(Guid enrollmentId, CancellationToken ct = default);
    Task<ClassInfo?> GetClassAsync(Guid classId, CancellationToken ct = default);
    Task<IReadOnlyList<EnrollmentInfo>> ListForClassAsync(Guid classId, CancellationToken ct = default);
    Task<IReadOnlyList<EnrollmentInfo>> ListForTermAsync(Guid termId, CancellationToken ct = default);
    Task<bool> TermExistsAsync(Guid termId, CancellationToken ct = default);
}
