namespace EEMP.Modules.Tenancy.Application;

public sealed record TenantDto(
    Guid Id, string Slug, string Name, string DefaultLocale, IReadOnlyList<string> SupportedLocales,
    string TimeZone, string Currency, string Status, DateTime CreatedAt, string RowVersion);

public sealed record TenantAdminUserRequest(string? UserName, string? DisplayName, string? Email, string? Phone, string? Password);

public sealed record CreateTenantRequest(
    string? Slug, string? Name, string? DefaultLocale, IReadOnlyList<string>? SupportedLocales,
    string? TimeZone, string? Currency, TenantAdminUserRequest? AdminUser);

public sealed record UpdateTenantRequest(
    string? Name, string? DefaultLocale, IReadOnlyList<string>? SupportedLocales,
    string? TimeZone, string? Currency, string? RowVersion);
