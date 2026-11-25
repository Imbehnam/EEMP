namespace EEMP.SharedKernel.Domain;

/// <summary>Marks an entity owned by exactly one tenant. TenantId is stamped from the server-side tenant context on save.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

/// <summary>Marks an aggregate protected by an optimistic concurrency token (SQL Server rowversion).</summary>
public interface IHasRowVersion
{
    byte[] RowVersion { get; set; }
}

/// <summary>Marks an entity with UTC audit timestamps maintained by the persistence pipeline.</summary>
public interface IHasTimestamps
{
    DateTime CreatedAt { get; set; }
    DateTime? UpdatedAt { get; set; }
}

/// <summary>Marks an entity that is explicitly classified as global (not tenant-owned).</summary>
public interface IGlobalEntity;

public abstract class TenantEntity : ITenantOwned, IHasRowVersion, IHasTimestamps
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Entity whose tenant is optional (e.g. user accounts: platform administrators have none).
/// The save pipeline rejects writes whose TenantId differs from the current tenant context.
/// </summary>
public interface IOptionalTenantOwned
{
    Guid? TenantId { get; }
}
