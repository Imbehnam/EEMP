using EEMP.SharedKernel.Domain;
using Microsoft.AspNetCore.Identity;

namespace EEMP.Modules.Identity.Domain;

/// <summary>
/// User account. Identities are isolated per tenant (accepted assumption OD-004): user name is unique within a tenant.
/// Platform administrators have no tenant. A user account is not a person record (People module links by UserId).
/// </summary>
public sealed class AppUser : IdentityUser<Guid>, IOptionalTenantOwned, IHasRowVersion, IHasTimestamps
{
    public Guid? TenantId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Locale { get; set; } = "fa-IR";
    public string TimeZone { get; set; } = "Asia/Tehran";
    public bool IsActive { get; set; } = true;
    public bool IsPlatformAdmin { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Tenant-defined bundle of permission codes. System roles are created at provisioning and are immutable.</summary>
public sealed class Role : TenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public List<RolePermission> Permissions { get; set; } = [];

    public void SetPermissions(IEnumerable<string> codes)
    {
        var wanted = codes.Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        Permissions.RemoveAll(p => !wanted.Contains(p.PermissionCode));
        foreach (var code in wanted.Where(c => Permissions.All(p => p.PermissionCode != c)))
            Permissions.Add(new RolePermission { TenantId = TenantId, RoleId = Id, PermissionCode = code });
    }
}

public sealed class RolePermission : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid RoleId { get; set; }
    public string PermissionCode { get; set; } = string.Empty;
}

/// <summary>Grants a role to a user, either tenant-wide (BranchId null) or for one branch. Scope never inherits to sibling branches.</summary>
public sealed class RoleAssignment : ITenantOwned, IHasTimestamps
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? GrantedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Role? Role { get; set; }
}

/// <summary>
/// Rotating refresh token. Only a SHA-256 hash is stored. FamilyId is the session id carried in access tokens (sid);
/// revoking the family ends the session on the next request. Reuse of a consumed token revokes the whole family.
/// </summary>
[NotAudited]
public sealed class RefreshToken : IOptionalTenantOwned, IGlobalEntity, IHasRowVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TenantId { get; set; }
    public Guid FamilyId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public static class SystemRoles
{
    public const string TenantAdmin = "TenantAdmin";
    public const string BranchManager = "BranchManager";
    public const string Teacher = "Teacher";
    public const string Guardian = "Guardian";
    public const string Student = "Student";
}
