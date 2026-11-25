namespace EEMP.SharedKernel.Errors;

/// <summary>
/// Base exception carrying a stable error code. Clients translate <see cref="Code"/>; server text is only a localized fallback.
/// </summary>
public class AppException : Exception
{
    public AppException(string code, int statusCode, IReadOnlyDictionary<string, object?>? parameters = null)
        : base(code)
    {
        Code = code;
        StatusCode = statusCode;
        Parameters = parameters ?? new Dictionary<string, object?>();
    }

    public string Code { get; }
    public int StatusCode { get; }
    public IReadOnlyDictionary<string, object?> Parameters { get; }
}

public sealed class NotFoundException(string code = ErrorCodes.NotFound, IReadOnlyDictionary<string, object?>? parameters = null)
    : AppException(code, 404, parameters);

public sealed class ForbiddenException(string code = ErrorCodes.Forbidden, IReadOnlyDictionary<string, object?>? parameters = null)
    : AppException(code, 403, parameters);

public sealed class ConflictException(string code, IReadOnlyDictionary<string, object?>? parameters = null)
    : AppException(code, 409, parameters);

public sealed class UnauthorizedException(string code = ErrorCodes.Unauthorized)
    : AppException(code, 401);

public sealed class BusinessRuleException(string code, IReadOnlyDictionary<string, object?>? parameters = null)
    : AppException(code, 422, parameters);

public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors)
    : AppException(ErrorCodes.ValidationFailed, 400)
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public static class ErrorCodes
{
    public const string ValidationFailed = "validation.failed";
    public const string NotFound = "resource.not_found";
    public const string Forbidden = "authorization.forbidden";
    public const string ForbiddenScope = "authorization.forbidden_scope";
    public const string SelfGrantForbidden = "authorization.self_grant_forbidden";
    public const string Unauthorized = "auth.unauthorized";
    public const string InvalidCredentials = "auth.invalid_credentials";
    public const string InvalidRefreshToken = "auth.invalid_refresh_token";
    public const string RefreshTokenReused = "auth.refresh_token_reused";
    public const string AccountInactive = "auth.account_inactive";
    public const string SessionRevoked = "auth.session_revoked";
    public const string TenantForbidden = "tenant.forbidden";
    public const string TenantSuspended = "tenant.suspended";
    public const string TenantSlugTaken = "tenant.slug_taken";
    public const string ConcurrencyConflict = "concurrency.conflict";
    public const string DuplicateValue = "resource.duplicate";
    public const string RateLimited = "rate_limit.exceeded";
    public const string InternalError = "server.error";
    public const string BranchInUse = "branch.in_use";
    public const string BranchCodeTaken = "branch.code_taken";
    public const string RoleSystemImmutable = "role.system_immutable";
    public const string RoleInUse = "role.in_use";
    public const string RoleCodeTaken = "role.code_taken";
    public const string UserNameTaken = "user.username_taken";
    public const string CannotDeactivateSelf = "user.cannot_deactivate_self";
    public const string PortalProfileNotFound = "portal.profile_not_found";
}
