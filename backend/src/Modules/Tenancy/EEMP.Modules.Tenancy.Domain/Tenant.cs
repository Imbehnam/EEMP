using EEMP.SharedKernel.Domain;

namespace EEMP.Modules.Tenancy.Domain;

public enum TenantStatus
{
    Active,
    Suspended,
}

/// <summary>One tenant = one institute group (accepted assumption OD-002). Classified global: it defines the tenant boundary.</summary>
public sealed class Tenant : IGlobalEntity, IHasRowVersion, IHasTimestamps
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DefaultLocale { get; set; } = "fa-IR";
    public List<string> SupportedLocales { get; set; } = [];
    public string TimeZone { get; set; } = "Asia/Tehran";
    public string Currency { get; set; } = "IRR";
    public TenantStatus Status { get; set; } = TenantStatus.Active;
    public byte[] RowVersion { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public void Suspend() => Status = TenantStatus.Suspended;
    public void Activate() => Status = TenantStatus.Active;
}
