using EEMP.Modules.Tenancy.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EEMP.Api;

/// <summary>Readiness: the database answers. No data is read (no tenant context is needed).</summary>
internal sealed class DatabaseHealthCheck(TenancyDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("database unreachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("database unreachable", ex);
        }
    }
}
