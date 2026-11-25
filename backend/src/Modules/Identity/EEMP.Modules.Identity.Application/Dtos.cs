namespace EEMP.Modules.Identity.Application;

public sealed record LoginRequest(string? TenantSlug, string? UserName, string? Password);
public sealed record RefreshRequest(string? RefreshToken);
public sealed record LogoutRequest(string? RefreshToken);
public sealed record PreferencesRequest(string? Locale, string? TimeZone);

public sealed record ScopeDto(string RoleCode, Guid? BranchId);

public sealed record MeDto(
    Guid Id, string UserName, string DisplayName, Guid? TenantId, string? TenantName, string Locale, string TimeZone,
    bool IsPlatformAdmin, IReadOnlyList<string> Permissions, IReadOnlyList<ScopeDto> Scopes);

public sealed record AuthResponse(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken, MeDto User);

public sealed record PermissionDto(string Code, string Module, bool IsSensitive, bool BranchScoped);

public sealed record RoleDto(Guid Id, string Code, string Name, bool IsSystem, IReadOnlyList<string> Permissions, string RowVersion);
public sealed record CreateRoleRequest(string? Code, string? Name, IReadOnlyList<string>? Permissions);
public sealed record UpdateRoleRequest(string? Name, IReadOnlyList<string>? Permissions, string? RowVersion);

public sealed record AssignmentDto(Guid RoleId, string RoleCode, Guid? BranchId);
public sealed record AssignmentRequest(Guid RoleId, Guid? BranchId);

public sealed record UserDto(
    Guid Id, string UserName, string DisplayName, string? Email, string? Phone, string Locale, string TimeZone,
    bool IsActive, IReadOnlyList<AssignmentDto> Assignments, DateTime CreatedAt, string RowVersion);

public sealed record CreateUserRequest(string? UserName, string? DisplayName, string? Email, string? Phone, string? Password, string? Locale, string? TimeZone);
public sealed record UpdateUserRequest(string? DisplayName, string? Email, string? Phone, string? Locale, string? TimeZone, string? RowVersion);
