using EEMP.Modules.Identity.Domain;
using EEMP.Modules.Tenancy.Contracts;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Tenancy;
using EEMP.SharedKernel.Validation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EEMP.Modules.Identity.Application;

public sealed class AuthService(
    IIdentityDbContext db,
    UserManager<AppUser> users,
    IPasswordHasher<AppUser> hasher,
    IAccessTokenIssuer tokens,
    ITenantDirectory tenants,
    ITenantContext tenantContext,
    IAuthAuditor auditor,
    TimeProvider clock)
{
    // Pre-computed hash used to equalize timing when the account does not exist (no account enumeration).
    private static string? _dummyHash;

    public async Task<AuthResponse> LoginAsync(LoginRequest req, CancellationToken ct)
    {
        new Validator()
            .RequiredText("userName", req.UserName, 256)
            .When(string.IsNullOrEmpty(req.Password), "password", Validator.Required)
            .ThrowIfInvalid();

        var slug = string.IsNullOrWhiteSpace(req.TenantSlug) ? null : req.TenantSlug.Trim().ToLowerInvariant();
        TenantInfo? tenant = null;
        if (slug is not null)
        {
            tenant = await tenants.FindBySlugAsync(slug, ct);
            if (tenant is null) return await FailAsync(null, null, ct);
        }

        using var scope = tenantContext.BeginTenantScope(tenant?.Id);
        var normalized = users.NormalizeName(req.UserName!.Trim());
        var user = tenant is null
            // Platform administrators have no tenant, so the tenant filter is bypassed with an explicit platform predicate.
            ? await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.TenantId == null && u.IsPlatformAdmin && u.NormalizedUserName == normalized, ct)
            : await db.Users.FirstOrDefaultAsync(u => u.NormalizedUserName == normalized, ct);

        if (user is null) return await FailAsync(tenant?.Id, null, ct);
        if (await users.IsLockedOutAsync(user)) return await FailAsync(tenant?.Id, user.Id, ct, "locked_out");
        if (!await users.CheckPasswordAsync(user, req.Password!))
        {
            await users.AccessFailedAsync(user);
            return await FailAsync(tenant?.Id, user.Id, ct);
        }
        await users.ResetAccessFailedCountAsync(user);

        if (!user.IsActive) throw new ForbiddenException(ErrorCodes.AccountInactive);
        if (tenant is { IsActive: false }) throw new ForbiddenException(ErrorCodes.TenantSuspended);

        var response = await IssueAsync(user, Guid.NewGuid(), tenant, ct);
        await auditor.RecordAsync("auth.login", user.TenantId, user.Id, "success", ct);
        return response;
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.RefreshToken)) throw new UnauthorizedException(ErrorCodes.InvalidRefreshToken);
        var hash = TokenHasher.Hash(req.RefreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
            ?? throw new UnauthorizedException(ErrorCodes.InvalidRefreshToken);

        using var scope = tenantContext.BeginTenantScope(token.TenantId);
        var now = clock.GetUtcNow().UtcDateTime;

        if (token.ConsumedAt is not null)
        {
            // Replay of a rotated token: assume theft and revoke the whole session family.
            await RevokeFamilyAsync(token.FamilyId, now, ct);
            await auditor.RecordAsync("auth.refresh_reuse", token.TenantId, token.UserId, "denied", ct);
            throw new UnauthorizedException(ErrorCodes.RefreshTokenReused);
        }
        if (token.RevokedAt is not null || token.ExpiresAt <= now) throw new UnauthorizedException(ErrorCodes.InvalidRefreshToken);

        var user = await LoadUserAsync(token.UserId, token.TenantId, ct);
        TenantInfo? tenant = token.TenantId is { } tid ? await tenants.FindByIdAsync(tid, ct) : null;
        if (user is null || !user.IsActive || (token.TenantId is not null && tenant is not { IsActive: true }))
        {
            await RevokeFamilyAsync(token.FamilyId, now, ct);
            throw new UnauthorizedException(ErrorCodes.InvalidRefreshToken);
        }

        token.ConsumedAt = now;
        var response = await IssueAsync(user, token.FamilyId, tenant, ct, token);
        return response;
    }

    public async Task LogoutAsync(LogoutRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.RefreshToken)) return;
        var hash = TokenHasher.Hash(req.RefreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null) return;
        using var scope = tenantContext.BeginTenantScope(token.TenantId);
        await RevokeFamilyAsync(token.FamilyId, clock.GetUtcNow().UtcDateTime, ct);
        await auditor.RecordAsync("auth.logout", token.TenantId, token.UserId, "success", ct);
    }

    /// <summary>Called for every authenticated request: the token is only valid while user, tenant and session are active.</summary>
    public async Task<SessionValidation> ValidateSessionAsync(Guid userId, Guid? tenantId, Guid sessionId, CancellationToken ct)
    {
        var user = await LoadUserAsync(userId, tenantId, ct);
        if (user is null || !user.IsActive) return new(ErrorCodes.SessionRevoked, false);
        if (user.IsPlatformAdmin != (tenantId is null)) return new(ErrorCodes.SessionRevoked, false);
        if (tenantId is { } tid)
        {
            var tenant = await tenants.FindByIdAsync(tid, ct);
            if (tenant is not { IsActive: true }) return new(ErrorCodes.TenantSuspended, false);
        }
        var now = clock.GetUtcNow().UtcDateTime;
        var alive = await db.RefreshTokens.AnyAsync(t => t.FamilyId == sessionId && t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now, ct);
        return alive ? new(null, user.IsPlatformAdmin) : new(ErrorCodes.SessionRevoked, false);
    }

    public async Task<MeDto> GetMeAsync(CancellationToken ct)
    {
        var userId = tenantContext.UserId ?? throw new UnauthorizedException();
        var user = await LoadUserAsync(userId, tenantContext.TenantId, ct) ?? throw new UnauthorizedException();
        TenantInfo? tenant = user.TenantId is { } tid ? await tenants.FindByIdAsync(tid, ct) : null;
        return await BuildMeAsync(user, tenant, ct);
    }

    public async Task<MeDto> UpdatePreferencesAsync(PreferencesRequest req, CancellationToken ct)
    {
        new Validator().Locale("locale", req.Locale).TimeZone("timeZone", req.TimeZone).ThrowIfInvalid();
        var userId = tenantContext.UserId ?? throw new UnauthorizedException();
        var user = await LoadUserAsync(userId, tenantContext.TenantId, ct) ?? throw new UnauthorizedException();
        user.Locale = req.Locale!;
        user.TimeZone = req.TimeZone!;
        await db.SaveChangesAsync(ct);
        TenantInfo? tenant = user.TenantId is { } tid ? await tenants.FindByIdAsync(tid, ct) : null;
        return await BuildMeAsync(user, tenant, ct);
    }

    private async Task<AuthResponse> IssueAsync(AppUser user, Guid familyId, TenantInfo? tenant, CancellationToken ct, RefreshToken? replaces = null)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var raw = TokenHasher.NewToken();
        var refresh = new RefreshToken
        {
            UserId = user.Id,
            TenantId = user.TenantId,
            FamilyId = familyId,
            TokenHash = TokenHasher.Hash(raw),
            CreatedAt = now,
            ExpiresAt = now.Add(tokens.RefreshTokenLifetime),
        };
        db.RefreshTokens.Add(refresh);
        if (replaces is not null) replaces.ReplacedByTokenId = refresh.Id;
        await db.SaveChangesAsync(ct);

        var access = tokens.Issue(user, familyId);
        return new AuthResponse(access.Token, access.ExpiresAt, raw, await BuildMeAsync(user, tenant, ct));
    }

    private async Task<MeDto> BuildMeAsync(AppUser user, TenantInfo? tenant, CancellationToken ct)
    {
        List<string> permissions = [];
        List<ScopeDto> scopes = [];
        if (user.TenantId is not null)
        {
            using var scope = tenantContext.BeginTenantScope(user.TenantId);
            var assignments = await db.RoleAssignments.Where(a => a.UserId == user.Id)
                .Select(a => new { a.RoleId, a.BranchId, a.Role!.Code }).ToListAsync(ct);
            var roleIds = assignments.Select(a => a.RoleId).Distinct().ToList();
            permissions = await db.RolePermissions.Where(p => roleIds.Contains(p.RoleId))
                .Select(p => p.PermissionCode).Distinct().OrderBy(p => p).ToListAsync(ct);
            scopes = assignments.Select(a => new ScopeDto(a.Code, a.BranchId)).ToList();
        }
        return new MeDto(user.Id, user.UserName!, user.DisplayName, user.TenantId, tenant?.Name, user.Locale, user.TimeZone,
            user.IsPlatformAdmin, permissions, scopes);
    }

    private Task<AppUser?> LoadUserAsync(Guid userId, Guid? tenantId, CancellationToken ct) =>
        // Explicit tenant predicate (user id + tenant id from the validated token) replaces the ambient filter here,
        // because this runs before the request tenant context is established.
        db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId, ct);

    private async Task RevokeFamilyAsync(Guid familyId, DateTime now, CancellationToken ct)
    {
        var family = await db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in family) t.RevokedAt = now;
        await db.SaveChangesAsync(ct);
    }

    private async Task<AuthResponse> FailAsync(Guid? tenantId, Guid? userId, CancellationToken ct, string result = "failure")
    {
        if (userId is null)
        {
            _dummyHash ??= hasher.HashPassword(new AppUser(), Guid.NewGuid().ToString());
            hasher.VerifyHashedPassword(new AppUser(), _dummyHash, "not-the-password");
        }
        await auditor.RecordAsync("auth.login", tenantId, userId, result, ct);
        throw new UnauthorizedException(ErrorCodes.InvalidCredentials);
    }
}

public sealed record SessionValidation(string? Error, bool IsPlatformAdmin);

/// <summary>Writes security audit events for authentication (no user names, passwords or tokens are recorded).</summary>
public interface IAuthAuditor
{
    Task RecordAsync(string action, Guid? tenantId, Guid? userId, string result, CancellationToken ct);
}
