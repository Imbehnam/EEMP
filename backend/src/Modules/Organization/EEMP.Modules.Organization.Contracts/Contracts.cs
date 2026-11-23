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
}
