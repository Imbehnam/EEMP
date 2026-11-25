namespace EEMP.SharedKernel.Tenancy;

/// <summary>
/// Request-scoped, server-established tenant and actor context. The tenant comes only from the validated
/// access token (tenant_id claim) or from an explicit system scope; never from client body/query/headers.
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
    Guid? UserId { get; }
    bool IsPlatformAdmin { get; }
    bool IsAuthenticated { get; }

    /// <summary>Establishes the context from a validated principal. Can only be called once per scope.</summary>
    void Initialize(Guid? tenantId, Guid userId, bool isPlatformAdmin);

    /// <summary>Temporarily runs code inside a specific tenant (system operations such as provisioning or seeding).</summary>
    IDisposable BeginTenantScope(Guid? tenantId);

    /// <summary>Returns the tenant id or throws when no tenant context exists (fail closed).</summary>
    Guid RequireTenantId();
}
