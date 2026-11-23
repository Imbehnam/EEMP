using EEMP.SharedKernel.Domain;

namespace EEMP.Modules.Organization.Domain;

/// <summary>The tenant's central office (accepted assumption OD-003: exactly one per tenant).</summary>
public sealed class CentralOffice : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string DefaultLocale { get; set; } = "fa-IR";
    public string TimeZone { get; set; } = "Asia/Tehran";
}

/// <summary>A branch of the institute group. Branch-scoped records carry BranchId; sibling branches are isolated.</summary>
public sealed class Branch : TenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
}
