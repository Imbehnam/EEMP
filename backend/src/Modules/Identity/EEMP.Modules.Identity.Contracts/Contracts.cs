namespace EEMP.Modules.Identity.Contracts;

public sealed record NewUserSpec(string UserName, string DisplayName, string? Email, string? Phone, string Password, string Locale, string TimeZone);

/// <summary>Creates the tenant's system roles and its first administrator (called during tenant provisioning, inside a tenant scope).</summary>
public interface ITenantAccessProvisioner
{
    Task<Guid> ProvisionTenantAsync(Guid tenantId, NewUserSpec admin, CancellationToken ct = default);

    /// <summary>Returns password-policy error codes (empty when acceptable), so provisioning can validate before writing.</summary>
    Task<IReadOnlyList<string>> ValidatePasswordAsync(string password, CancellationToken ct = default);
}

/// <summary>Lookups of user accounts in the current tenant for other modules.</summary>
public interface IUserDirectory
{
    Task<bool> ExistsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Display names of users in the current tenant (missing ids are omitted). No permission check: callers authorize.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);
}

/// <summary>Lets other modules know whether a branch is referenced by role assignments.</summary>
public interface IAssignmentUsage
{
    Task<bool> IsBranchReferencedAsync(Guid branchId, CancellationToken ct = default);
}
