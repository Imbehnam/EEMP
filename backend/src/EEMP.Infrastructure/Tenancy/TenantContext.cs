using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Tenancy;

namespace EEMP.Infrastructure.Tenancy;

public sealed class TenantContext : ITenantContext
{
    private Guid? _tenantId;
    private Guid? _scopedTenantId;
    private bool _scoped;
    private bool _initialized;

    public Guid? TenantId => _scoped ? _scopedTenantId : _tenantId;
    public Guid? UserId { get; private set; }
    public bool IsPlatformAdmin { get; private set; }
    public bool IsAuthenticated => _initialized;

    public void Initialize(Guid? tenantId, Guid userId, bool isPlatformAdmin)
    {
        if (_initialized) throw new InvalidOperationException("Tenant context is already initialized for this scope.");
        if (isPlatformAdmin && tenantId is not null)
            throw new InvalidOperationException("Platform administrators have no tenant context.");
        _tenantId = tenantId;
        UserId = userId;
        IsPlatformAdmin = isPlatformAdmin;
        _initialized = true;
    }

    public IDisposable BeginTenantScope(Guid? tenantId)
    {
        var previous = (_scoped, _scopedTenantId);
        _scoped = true;
        _scopedTenantId = tenantId;
        return new Restore(() => (_scoped, _scopedTenantId) = previous);
    }

    public Guid RequireTenantId() => TenantId ?? throw new ForbiddenException(ErrorCodes.TenantForbidden);

    private sealed class Restore(Action action) : IDisposable
    {
        private Action? _action = action;
        public void Dispose()
        {
            _action?.Invoke();
            _action = null;
        }
    }
}
