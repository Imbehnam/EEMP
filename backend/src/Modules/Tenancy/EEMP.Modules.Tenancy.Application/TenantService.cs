using System.Text.RegularExpressions;
using EEMP.Modules.Identity.Contracts;
using EEMP.Modules.Organization.Contracts;
using EEMP.Modules.Tenancy.Domain;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Paging;
using EEMP.SharedKernel.Tenancy;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;

namespace EEMP.Modules.Tenancy.Application;

/// <summary>Platform tenant lifecycle. Callers must be platform administrators (enforced at the endpoint policy).</summary>
public sealed partial class TenantService(
    ITenancyDbContext db,
    ITenantContext tenantContext,
    ITenantAccessProvisioner accessProvisioner,
    IOrganizationProvisioner organizationProvisioner)
{
    public async Task<PagedResult<TenantDto>> ListAsync(PageRequest page, CancellationToken ct)
    {
        var q = db.Tenants.AsNoTracking();
        if (page.Search is { } s) q = q.Where(t => t.Name.Contains(s) || t.Slug.Contains(s));
        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(t => t.Name).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<TenantDto>(items.Select(ToDto).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<TenantDto> GetAsync(Guid id, CancellationToken ct) => ToDto(await FindAsync(id, ct));

    public async Task<TenantDto> CreateAsync(CreateTenantRequest req, CancellationToken ct)
    {
        var v = new Validator()
            .Slug("slug", req.Slug)
            .RequiredText("name", req.Name, 200)
            .Locale("defaultLocale", req.DefaultLocale ?? SupportedLocales.Default)
            .TimeZone("timeZone", req.TimeZone ?? "Asia/Tehran");
        ValidateLocales(v, req.SupportedLocales, req.DefaultLocale ?? SupportedLocales.Default);
        ValidateCurrency(v, req.Currency ?? "IRR");
        if (req.AdminUser is null) v.Add("adminUser", Validator.Required);
        else
        {
            v.RequiredText("adminUser.userName", req.AdminUser.UserName, 100)
             .RequiredText("adminUser.displayName", req.AdminUser.DisplayName, 200)
             .Email("adminUser.email", req.AdminUser.Email)
             .Phone("adminUser.phone", req.AdminUser.Phone)
             .When(string.IsNullOrEmpty(req.AdminUser.Password), "adminUser.password", Validator.Required);
        }
        v.ThrowIfInvalid();
        foreach (var code in await accessProvisioner.ValidatePasswordAsync(req.AdminUser!.Password!, ct))
            v.Add("adminUser.password", code);
        v.ThrowIfInvalid();

        var slug = req.Slug!.Trim();
        if (await db.Tenants.AnyAsync(t => t.Slug == slug, ct)) throw new ConflictException(ErrorCodes.TenantSlugTaken);

        var tenant = new Tenant
        {
            Slug = slug,
            Name = req.Name!.Trim(),
            DefaultLocale = req.DefaultLocale ?? SupportedLocales.Default,
            SupportedLocales = (req.SupportedLocales is { Count: > 0 } l ? l : SupportedLocales.All).Distinct().ToList(),
            TimeZone = req.TimeZone ?? "Asia/Tehran",
            Currency = (req.Currency ?? "IRR").ToUpperInvariant(),
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);

        // Provision tenant-scoped data inside an explicit system tenant scope.
        // Not one DB transaction across module contexts (known gap, see DECISION_LOG); failures surface as 500 and are logged.
        using (tenantContext.BeginTenantScope(tenant.Id))
        {
            await organizationProvisioner.ProvisionAsync(tenant.Id, tenant.Name, tenant.DefaultLocale, tenant.TimeZone, ct);
            var a = req.AdminUser!;
            await accessProvisioner.ProvisionTenantAsync(tenant.Id,
                new NewUserSpec(a.UserName!.Trim(), a.DisplayName!.Trim(), a.Email, a.Phone, a.Password!, tenant.DefaultLocale, tenant.TimeZone), ct);
        }

        return ToDto(tenant);
    }

    public async Task<TenantDto> UpdateAsync(Guid id, UpdateTenantRequest req, CancellationToken ct)
    {
        var v = new Validator()
            .RequiredText("name", req.Name, 200)
            .Locale("defaultLocale", req.DefaultLocale)
            .TimeZone("timeZone", req.TimeZone)
            .RowVersion("rowVersion", req.RowVersion);
        ValidateLocales(v, req.SupportedLocales, req.DefaultLocale ?? SupportedLocales.Default);
        ValidateCurrency(v, req.Currency);
        v.ThrowIfInvalid();

        var tenant = await FindAsync(id, ct);
        db.SetOriginalRowVersion(tenant, Convert.FromBase64String(req.RowVersion!));
        tenant.Name = req.Name!.Trim();
        tenant.DefaultLocale = req.DefaultLocale!;
        tenant.SupportedLocales = (req.SupportedLocales is { Count: > 0 } l ? l : SupportedLocales.All).Distinct().ToList();
        tenant.TimeZone = req.TimeZone!;
        tenant.Currency = req.Currency!.ToUpperInvariant();
        await db.SaveChangesAsync(ct);
        return ToDto(tenant);
    }

    public async Task<TenantDto> SetStatusAsync(Guid id, bool active, CancellationToken ct)
    {
        var tenant = await FindAsync(id, ct);
        if (active) tenant.Activate(); else tenant.Suspend();
        await db.SaveChangesAsync(ct);
        return ToDto(tenant);
    }

    private async Task<Tenant> FindAsync(Guid id, CancellationToken ct) =>
        await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException();

    private static void ValidateLocales(Validator v, IReadOnlyList<string>? locales, string defaultLocale)
    {
        if (locales is null || locales.Count == 0) return;
        if (locales.Any(l => !SupportedLocales.IsSupported(l))) v.Add("supportedLocales", Validator.UnsupportedLocale);
        if (!locales.Contains(defaultLocale)) v.Add("supportedLocales", Validator.InvalidValue);
    }

    private static void ValidateCurrency(Validator v, string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency)) v.Add("currency", Validator.Required);
        else if (!CurrencyRegex().IsMatch(currency)) v.Add("currency", Validator.InvalidFormat);
    }

    public static TenantDto ToDto(Tenant t) => new(
        t.Id, t.Slug, t.Name, t.DefaultLocale, t.SupportedLocales, t.TimeZone, t.Currency,
        t.Status.ToString().ToLowerInvariant(), t.CreatedAt, Convert.ToBase64String(t.RowVersion));

    [GeneratedRegex("^[A-Za-z]{3}$")]
    private static partial Regex CurrencyRegex();
}
