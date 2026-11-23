using EEMP.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace EEMP.Modules.Identity.Application;

public interface IIdentityDbContext
{
    DbSet<AppUser> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<RoleAssignment> RoleAssignments { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    void SetOriginalRowVersion<T>(T entity, byte[] rowVersion) where T : class;
}

public sealed record IssuedAccessToken(string Token, DateTime ExpiresAt);

/// <summary>Issues signed access tokens. Tokens carry identity, tenant and session id only — never permissions.</summary>
public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(AppUser user, Guid sessionId);
    TimeSpan RefreshTokenLifetime { get; }
}
