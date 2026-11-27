using System.Linq.Expressions;
using System.Text.Json;
using EEMP.Modules.Consultation.Contracts;
using EEMP.Modules.Consultation.Domain;
using EEMP.Modules.Organization.Contracts;
using EEMP.Modules.People.Contracts;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace EEMP.Modules.Consultation.Application;

public interface IConsultationDbContext
{
    DbSet<ConsultationSettings> Settings { get; }
    DbSet<Consultant> Consultants { get; }
    DbSet<ConsultantBranch> ConsultantBranches { get; }
    DbSet<Slot> Slots { get; }
    DbSet<Booking> Bookings { get; }
    DbSet<BookingNote> BookingNotes { get; }
    DbSet<Ticket> Tickets { get; }
    DbSet<TicketMessage> TicketMessages { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    void SetOriginalRowVersion<T>(T entity, byte[] rowVersion) where T : class;
    void ResetTracking();
}

/// <summary>Current UTC time for consultation rules. A seam so tests can move time without touching token validation.</summary>
public interface IConsultationClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemConsultationClock(TimeProvider time) : IConsultationClock
{
    public DateTime UtcNow => time.GetUtcNow().UtcDateTime;
}

/// <summary>Writes access to confidential notes to the audit trail (ids only, never note text).</summary>
public interface INotesAccessLog
{
    Task NotesReadAsync(Guid bookingId, int count, CancellationToken ct = default);
}

public static class Fmt
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string Camel<TEnum>(TEnum value) where TEnum : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
    public static string? Camel<TEnum>(TEnum? value) where TEnum : struct, Enum => value is { } v ? Camel(v) : null;
    public static string Rv(byte[] rv) => Convert.ToBase64String(rv);
    public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    public static Dictionary<string, object?> P(params (string Key, object? Value)[] items) => items.ToDictionary(i => i.Key, i => i.Value);

    /// <summary>Parses a camelCase enum name strictly (no numbers).</summary>
    public static bool TryEnum<TEnum>(string? value, out TEnum result) where TEnum : struct, Enum
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value) || int.TryParse(value, out _)) return false;
        return Enum.TryParse(value.Replace("-", "", StringComparison.Ordinal), true, out result) && Enum.IsDefined(result);
    }

    public static string StudentName(StudentSummary? s) => s is null ? string.Empty : $"{s.FirstName} {s.LastName}".Trim();
}

/// <summary>
/// The caller's consultation capabilities, resolved per request: consultant profile and the branch scopes of each permission.
/// Grants are read from the database on every request (no cross-request cache).
/// </summary>
public sealed class ConsultationActor(IConsultationDbContext db, IAccessControl access, ITenantContext tenant, IPeopleDirectory people,
    IOrganizationSettings settings)
{
    private (bool Loaded, Consultant? Value) _consultant;
    private readonly Dictionary<string, BranchScope> _scopes = new(StringComparer.Ordinal);
    private IReadOnlySet<Guid>? _portalStudents;

    public Guid UserId => tenant.UserId ?? throw new UnauthorizedException();

    public async Task<Consultant?> ConsultantAsync(CancellationToken ct)
    {
        if (_consultant.Loaded) return _consultant.Value;
        var me = UserId;
        var c = await db.Consultants.AsNoTracking().Include(x => x.Branches).FirstOrDefaultAsync(x => x.UserId == me, ct);
        _consultant = (true, c);
        return c;
    }

    public async Task<BranchScope> ScopeAsync(string permission, CancellationToken ct)
    {
        if (_scopes.TryGetValue(permission, out var s)) return s;
        s = await access.GetBranchScopeAsync(permission, ct);
        _scopes[permission] = s;
        return s;
    }

    /// <summary>True when the caller is this consultant and holds consultation.sessions.manage in the branch.</summary>
    public async Task<bool> IsOwnAsync(Guid consultantId, Guid branchId, CancellationToken ct)
    {
        var me = await ConsultantAsync(ct);
        return me is not null && me.Id == consultantId && (await ScopeAsync(Permissions.ConsultationSessionsManage, ct)).Includes(branchId);
    }

    /// <summary>Union of the scopes of the given permissions.</summary>
    public async Task<BranchScope> AnyScopeAsync(CancellationToken ct, params string[] permissions)
    {
        var ids = new HashSet<Guid>();
        foreach (var p in permissions)
        {
            var s = await ScopeAsync(p, ct);
            if (s.AllBranches) return BranchScope.Tenant;
            ids.UnionWith(s.BranchIds);
        }
        return ids.Count == 0 ? BranchScope.None : new BranchScope(false, ids);
    }

    /// <summary>Students the caller may act for in the portal: themself (student) and linked students (guardian).</summary>
    public async Task<IReadOnlySet<Guid>> PortalStudentIdsAsync(CancellationToken ct)
    {
        if (_portalStudents is not null) return _portalStudents;
        var uid = UserId;
        var ids = new HashSet<Guid>(await people.GetGuardianStudentIdsForUserAsync(uid, ct));
        if (await people.FindStudentIdForUserAsync(uid, ct) is { } own) ids.Add(own);
        return _portalStudents = ids;
    }

    /// <summary>The linked student, or 404 (existence of other students is not revealed).</summary>
    public async Task<StudentSummary> PortalStudentAsync(Guid studentId, CancellationToken ct)
    {
        if (!(await PortalStudentIdsAsync(ct)).Contains(studentId)) throw new NotFoundException();
        var s = await people.GetStudentsAsync([studentId], ct);
        return s.GetValueOrDefault(studentId) ?? throw new NotFoundException();
    }

    public async Task<TimeZoneInfo> TimeZoneAsync(CancellationToken ct)
    {
        var id = await settings.GetTimeZoneAsync(ct);
        return TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz) ? tz : TimeZoneInfo.Utc;
    }
}

internal static class ScopeFilter
{
    /// <summary>e => scope covers e.BranchId (AllBranches ⇒ true, empty ⇒ false).</summary>
    public static Expression<Func<T, bool>> Covers<T>(BranchScope scope, Expression<Func<T, Guid>> branch)
    {
        var p = branch.Parameters[0];
        if (scope.AllBranches) return Expression.Lambda<Func<T, bool>>(Expression.Constant(true), p);
        var ids = scope.BranchIds.ToList();
        var contains = Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(Guid)], Expression.Constant(ids), branch.Body);
        return Expression.Lambda<Func<T, bool>>(contains, p);
    }

    public static Expression<Func<T, bool>> Or<T>(this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        var p = left.Parameters[0];
        var body = new Replace(right.Parameters[0], p).Visit(right.Body)!;
        return Expression.Lambda<Func<T, bool>>(Expression.OrElse(left.Body, body), p);
    }

    private sealed class Replace(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}

public sealed class ConsultationBranchUsage(IConsultationDbContext db) : IConsultationBranchUsage
{
    public async Task<bool> IsBranchReferencedAsync(Guid branchId, CancellationToken ct = default) =>
        await db.ConsultantBranches.AnyAsync(b => b.BranchId == branchId, ct) || await db.Slots.AnyAsync(s => s.BranchId == branchId, ct)
        || await db.Tickets.AnyAsync(t => t.BranchId == branchId, ct);
}
