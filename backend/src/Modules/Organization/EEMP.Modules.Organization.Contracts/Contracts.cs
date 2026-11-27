namespace EEMP.Modules.Organization.Contracts;

/// <summary>Creates the central office record for a new tenant (called inside the new tenant's scope).</summary>
public interface IOrganizationProvisioner
{
    Task ProvisionAsync(Guid tenantId, string name, string defaultLocale, string timeZone, CancellationToken ct = default);
}

/// <summary>Branch lookups in the current tenant for other modules.</summary>
public interface IBranchDirectory
{
    Task<bool> ExistsAsync(Guid branchId, CancellationToken ct = default);

    /// <summary>Display name of a branch in the current tenant, or null when it does not exist.</summary>
    Task<string?> GetNameAsync(Guid branchId, CancellationToken ct = default);
}

/// <summary>Tenant-level settings other modules need (e.g. the time zone that defines "today" for calendar dates).</summary>
public interface IOrganizationSettings
{
    /// <summary>IANA/Windows time zone id of the current tenant's central office; "Asia/Tehran" when not set.</summary>
    Task<string> GetTimeZoneAsync(CancellationToken ct = default);

    /// <summary>Display name of the current tenant's institute (central office), or an empty string when not provisioned.</summary>
    Task<string> GetInstituteNameAsync(CancellationToken ct = default);
}
