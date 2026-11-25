using EEMP.Infrastructure.Audit;
using EEMP.Modules.Identity.Application;

namespace EEMP.Modules.Identity.Infrastructure;

internal sealed class AuthAuditor(IAuditWriter writer) : IAuthAuditor
{
    public Task RecordAsync(string action, Guid? tenantId, Guid? userId, string result, CancellationToken ct) =>
        writer.WriteAsync([new AuditEntry(action, "Session", userId?.ToString(), tenantId, null, result, userId)], ct);
}
