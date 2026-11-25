namespace EEMP.Modules.People.Contracts;

/// <summary>Lets other modules know whether people records reference a branch.</summary>
public interface IPeopleBranchUsage
{
    Task<bool> IsBranchReferencedAsync(Guid branchId, CancellationToken ct = default);
}

public sealed record StudentSummary(Guid Id, Guid BranchId, string FirstName, string LastName, string StudentNumber, bool IsActive);

public sealed record TeacherSummary(Guid Id, Guid BranchId, string FirstName, string LastName, bool IsActive);

/// <summary>
/// Read-only person lookups in the current tenant for other modules (academic rosters, teacher/guardian/student relationships).
/// It performs no permission checks: callers authorize the surrounding operation themselves and return only what that allows.
/// </summary>
public interface IPeopleDirectory
{
    Task<IReadOnlyDictionary<Guid, StudentSummary>> GetStudentsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, TeacherSummary>> GetTeachersAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task<Guid?> FindTeacherIdForUserAsync(Guid userId, CancellationToken ct = default);
    Task<Guid?> FindStudentIdForUserAsync(Guid userId, CancellationToken ct = default);
    /// <summary>Students linked to the guardian profile of this user (empty when the user is not a guardian).</summary>
    Task<IReadOnlyList<Guid>> GetGuardianStudentIdsForUserAsync(Guid userId, CancellationToken ct = default);
}
