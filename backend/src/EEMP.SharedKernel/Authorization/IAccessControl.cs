namespace EEMP.SharedKernel.Authorization;

/// <summary>The set of branches in which the current user holds a permission.</summary>
public sealed record BranchScope(bool AllBranches, IReadOnlySet<Guid> BranchIds)
{
    public static readonly BranchScope None = new(false, new HashSet<Guid>());
    public static readonly BranchScope Tenant = new(true, new HashSet<Guid>());
    public bool IsEmpty => !AllBranches && BranchIds.Count == 0;
    public bool Includes(Guid branchId) => AllBranches || BranchIds.Contains(branchId);
}

/// <summary>
/// Central authorization service. Grants are re-read from the database on every request (no cross-request cache),
/// so revocation takes effect on the next authorization decision (security baseline #5).
/// </summary>
public interface IAccessControl
{
    /// <summary>True if the user holds the permission in any scope (tenant-wide or at least one branch).</summary>
    Task<bool> HasAnyAsync(string permission, CancellationToken ct = default);

    /// <summary>True if the user holds the permission tenant-wide (assignment with no branch).</summary>
    Task<bool> HasTenantWideAsync(string permission, CancellationToken ct = default);

    Task<BranchScope> GetBranchScopeAsync(string permission, CancellationToken ct = default);

    /// <summary>Throws 403 authorization.forbidden_scope unless the user holds the permission for the branch.</summary>
    Task EnsureBranchAsync(string permission, Guid branchId, CancellationToken ct = default);

    /// <summary>Throws 403 unless the user holds the permission tenant-wide.</summary>
    Task EnsureTenantWideAsync(string permission, CancellationToken ct = default);
}
