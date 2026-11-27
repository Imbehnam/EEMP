namespace EEMP.Modules.Tenancy.Contracts;

/// <summary>Tenant-level money settings for the finance module (DL-059).</summary>
public interface ITenantFinanceSettings
{
    /// <summary>ISO 4217 currency of the current tenant ("IRR" when not set).</summary>
    Task<string> GetCurrencyAsync(CancellationToken ct = default);
}
