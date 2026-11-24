namespace EEMP.Modules.Academic.Contracts;

/// <summary>Lets other modules know whether academic records reference a branch or a person (deletion guards).</summary>
public interface IAcademicUsage
{
    Task<bool> IsBranchReferencedAsync(Guid branchId, CancellationToken ct = default);
    Task<bool> IsStudentReferencedAsync(Guid studentId, CancellationToken ct = default);
    Task<bool> IsTeacherReferencedAsync(Guid teacherId, CancellationToken ct = default);
}
