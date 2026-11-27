using System.Globalization;
using System.Text.Json;
using EEMP.Modules.Finance.Domain;
using EEMP.Modules.Organization.Contracts;
using EEMP.Modules.People.Contracts;
using EEMP.Modules.Tenancy.Contracts;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Tenancy;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;

namespace EEMP.Modules.Finance.Application;

public interface IFinanceDbContext
{
    DbSet<FinanceSettings> Settings { get; }
    DbSet<FeeType> FeeTypes { get; }
    DbSet<FeeItem> FeeItems { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<InvoiceLine> InvoiceLines { get; }
    DbSet<InvoiceDiscount> Discounts { get; }
    DbSet<Installment> Installments { get; }
    DbSet<InvoiceAdjustment> Adjustments { get; }
    DbSet<GeneratedCharge> GeneratedCharges { get; }
    DbSet<Payment> Payments { get; }
    DbSet<PaymentAllocation> Allocations { get; }
    DbSet<Refund> Refunds { get; }
    DbSet<NumberSeries> NumberSeries { get; }
    DbSet<FinanceAuditEntry> Audit { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    void SetOriginalRowVersion<T>(T entity, byte[] rowVersion) where T : class;
    void ResetTracking();
}

/// <summary>JSON/format helpers matching the M1/M2 conventions.</summary>
public static class Fmt
{
    public static string Camel<TEnum>(TEnum value) where TEnum : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
    public static string? Camel<TEnum>(TEnum? value) where TEnum : struct, Enum => value is { } v ? Camel(v) : null;
    public static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string? Date(DateOnly? d) => d is { } v ? Date(v) : null;
    public static string Rv(byte[] rv) => Convert.ToBase64String(rv);
    public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    public static Dictionary<string, object?> P(params (string Key, object? Value)[] items) => items.ToDictionary(i => i.Key, i => i.Value);

    public static TEnum? ParseEnum<TEnum>(string? value, string field, Validator v, bool required = false) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required) v.Add(field, Validator.Required);
            return null;
        }
        if (!int.TryParse(value, out _) && Enum.TryParse<TEnum>(value, ignoreCase: true, out var e) && Enum.IsDefined(e)) return e;
        v.Add(field, Validator.InvalidValue);
        return null;
    }

    public static DateOnly? OptionalDate(string? value, string field, Validator v) =>
        string.IsNullOrWhiteSpace(value) ? null : Validator.ParseDate(value, field, v);

    public static byte[] RowVersion(string? value) => Convert.FromBase64String(value!);

    /// <summary>
    /// Compares the client's rowVersion with the loaded row (409 when stale). Checked in code rather than as the original value so a
    /// unit of work that lost an unrelated race (number series, balances) can be retried and re-checked.
    /// </summary>
    public static void CheckRowVersion(EEMP.SharedKernel.Domain.IHasRowVersion entity, string? clientValue)
    {
        new Validator().RowVersion("rowVersion", clientValue).ThrowIfInvalid();
        if (!entity.RowVersion.AsSpan().SequenceEqual(Convert.FromBase64String(clientValue!)))
            throw new ConflictException(ErrorCodes.ConcurrencyConflict);
    }

    /// <summary>Validates a client amount: positive and within the currency's minor unit.</summary>
    public static decimal? Amount(decimal? value, string field, string currency, Validator v, bool required = true, bool allowZero = false)
    {
        if (value is null) { if (required) v.Add(field, Validator.Required); return null; }
        if (value < 0 || (!allowZero && value == 0) || value > 1_000_000_000_000m || !Money.HasValidScale(value.Value, currency))
        {
            v.Add(field, Validator.InvalidValue);
            return null;
        }
        return value;
    }
}

/// <summary>Per-request finance actor: user, clock, tenant-local today, tenant currency and portal relationships.</summary>
public sealed class FinanceActor(ITenantContext tenant, IPeopleDirectory people, IOrganizationSettings org, ITenantFinanceSettings money, TimeProvider clock)
{
    private DateOnly? _today;
    private string? _currency;

    public Guid? UserId => tenant.UserId;
    public DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public async Task<DateOnly> TodayAsync(CancellationToken ct)
    {
        if (_today is { } t) return t;
        var tzId = await org.GetTimeZoneAsync(ct);
        var tz = TimeZoneInfo.TryFindSystemTimeZoneById(tzId, out var found) ? found : TimeZoneInfo.Utc;
        return (_today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), tz).DateTime)).Value;
    }

    public async Task<string> CurrencyAsync(CancellationToken ct) => _currency ??= await money.GetCurrencyAsync(ct);

    public async Task<IReadOnlySet<Guid>> PortalStudentIdsAsync(CancellationToken ct)
    {
        var uid = tenant.UserId ?? throw new UnauthorizedException();
        var ids = new HashSet<Guid>(await people.GetGuardianStudentIdsForUserAsync(uid, ct));
        if (await people.FindStudentIdForUserAsync(uid, ct) is { } own) ids.Add(own);
        return ids;
    }
}

/// <summary>Branch-scoped finance authorization: not readable → 404, readable but not permitted → 403 forbidden_scope.</summary>
public sealed class FinanceAccess(IAccessControl access)
{
    private readonly Dictionary<string, BranchScope> _cache = new(StringComparer.Ordinal);

    public async Task<BranchScope> ScopeAsync(string permission, CancellationToken ct)
    {
        if (_cache.TryGetValue(permission, out var s)) return s;
        return _cache[permission] = await access.GetBranchScopeAsync(permission, ct);
    }

    public async Task EnsureAsync(Guid branchId, string permission, CancellationToken ct)
    {
        if ((await ScopeAsync(permission, ct)).Includes(branchId)) return;
        if ((await ScopeAsync(Permissions.FinanceRead, ct)).Includes(branchId)) throw new ForbiddenException(ErrorCodes.ForbiddenScope);
        throw new NotFoundException();
    }

    public Task<bool> HasAnyAsync(string permission, CancellationToken ct) => access.HasAnyAsync(permission, ct);
}

/// <summary>Writes append-only finance audit rows into the current unit of work (DL-063).</summary>
public sealed class FinanceAuditor(IFinanceDbContext db, FinanceActor actor)
{
    public void Write(string action, string entityType, Guid entityId, Guid? invoiceId = null, Guid? paymentId = null, Guid? studentId = null,
        decimal? amount = null, string? currency = null, string? details = null, Guid? actorUserId = null) =>
        db.Audit.Add(new FinanceAuditEntry
        {
            At = actor.UtcNow, ActorUserId = actorUserId ?? actor.UserId, Action = action, EntityType = entityType, EntityId = entityId,
            InvoiceId = invoiceId, PaymentId = paymentId, StudentId = studentId, Amount = amount, Currency = currency,
            Details = details is { Length: > 400 } ? details[..400] : details,
        });
}

/// <summary>DL-060: gap-free numbers taken in the same SaveChanges as the document (the series row carries a rowversion).</summary>
public sealed class DocumentNumberer(IFinanceDbContext db)
{
    private readonly Dictionary<string, NumberSeries> _loaded = new(StringComparer.Ordinal);

    public async Task<string> NextAsync(string series, CancellationToken ct)
    {
        if (!_loaded.TryGetValue(series, out var row))
        {
            row = await db.NumberSeries.FirstOrDefaultAsync(s => s.Series == series, ct);
            if (row is null)
            {
                row = new NumberSeries { Series = series, LastValue = 0 };
                db.NumberSeries.Add(row);
            }
            _loaded[series] = row;
        }
        row.LastValue++;
        return DocumentNumbers.Format(series, row.LastValue);
    }

    public void Reset() => _loaded.Clear();
}

/// <summary>
/// Runs a write as one unit of work and retries it on a fresh change tracker when it lost a race (rowversion or unique key):
/// number series, invoice balances and idempotency keys are all re-read on the retry.
/// </summary>
public sealed class FinanceUnitOfWork(IFinanceDbContext db, DocumentNumberer numbers)
{
    public const int MaxAttempts = 6;

    public async Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await work();
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                db.ResetTracking();
                numbers.Reset();
                await Task.Delay(Random.Shared.Next(5, 25 * attempt), ct);
            }
        }
    }
}

public sealed record FinanceDisplay(string StudentName, string StudentNumber);

/// <summary>Student display names for finance DTOs (no permission check: callers authorize the finance record first).</summary>
public sealed class StudentNames(IPeopleDirectory people)
{
    public async Task<IReadOnlyDictionary<Guid, StudentSummary>> GetAsync(IEnumerable<Guid> ids, CancellationToken ct) =>
        await people.GetStudentsAsync(ids.Distinct().ToList(), ct);
}
