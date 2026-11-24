namespace EEMP.Modules.Tenancy.Contracts;

public sealed record TenantInfo(Guid Id, string Slug, string Name, bool IsActive, string DefaultLocale, string TimeZone);

/// <summary>Read-only tenant lookups for other modules (login by slug, session validation, /auth/me).</summary>
public interface ITenantDirectory
{
    Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct = default);
    Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct = default);
}
