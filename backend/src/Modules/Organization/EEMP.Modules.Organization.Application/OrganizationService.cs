using EEMP.Modules.Academic.Contracts;
using EEMP.Modules.Identity.Contracts;
using EEMP.Modules.Organization.Contracts;
using EEMP.Modules.Organization.Domain;
using EEMP.Modules.People.Contracts;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Paging;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;

namespace EEMP.Modules.Organization.Application;

public interface IOrganizationDbContext
{
    DbSet<CentralOffice> CentralOffices { get; }
    DbSet<Branch> Branches { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    void SetOriginalRowVersion<T>(T entity, byte[] rowVersion) where T : class;
}

public sealed record OrganizationDto(string Name, string? LegalName, string? Phone, string? Email, string? Address, string DefaultLocale, string TimeZone, string RowVersion);
public sealed record UpdateOrganizationRequest(string? Name, string? LegalName, string? Phone, string? Email, string? Address, string? DefaultLocale, string? TimeZone, string? RowVersion);

public sealed record BranchDto(Guid Id, string Code, string Name, string? Address, string? Phone, bool IsActive, string RowVersion);
public sealed record CreateBranchRequest(string? Code, string? Name, string? Address, string? Phone, bool? IsActive);
public sealed record UpdateBranchRequest(string? Code, string? Name, string? Address, string? Phone, bool? IsActive, string? RowVersion);

public sealed class OrganizationService(IOrganizationDbContext db, IAccessControl access)
{
    public async Task<OrganizationDto> GetAsync(CancellationToken ct)
    {
        await access.EnsureTenantWideAsync(Permissions.OrganizationRead, ct);
        return ToDto(await FindAsync(ct));
    }

    public async Task<OrganizationDto> UpdateAsync(UpdateOrganizationRequest req, CancellationToken ct)
    {
        await access.EnsureTenantWideAsync(Permissions.OrganizationManage, ct);
        new Validator()
            .RequiredText("name", req.Name, 200)
            .OptionalText("legalName", req.LegalName, 300)
            .Phone("phone", req.Phone)
            .Email("email", req.Email)
            .OptionalText("address", req.Address, 500)
            .Locale("defaultLocale", req.DefaultLocale)
            .TimeZone("timeZone", req.TimeZone)
            .RowVersion("rowVersion", req.RowVersion)
            .ThrowIfInvalid();
        var org = await FindAsync(ct);
        db.SetOriginalRowVersion(org, Convert.FromBase64String(req.RowVersion!));
        org.Name = req.Name!.Trim();
        org.LegalName = req.LegalName?.Trim();
        org.Phone = req.Phone?.Trim();
        org.Email = req.Email?.Trim();
        org.Address = req.Address?.Trim();
        org.DefaultLocale = req.DefaultLocale!;
        org.TimeZone = req.TimeZone!;
        await db.SaveChangesAsync(ct);
        return ToDto(org);
    }

    private async Task<CentralOffice> FindAsync(CancellationToken ct) =>
        await db.CentralOffices.FirstOrDefaultAsync(ct) ?? throw new NotFoundException();

    private static OrganizationDto ToDto(CentralOffice o) =>
        new(o.Name, o.LegalName, o.Phone, o.Email, o.Address, o.DefaultLocale, o.TimeZone, Convert.ToBase64String(o.RowVersion));
}

public sealed class BranchService(IOrganizationDbContext db, IAccessControl access, IPeopleBranchUsage peopleUsage, IAssignmentUsage assignmentUsage,
    IAcademicUsage academicUsage)
{
    public async Task<PagedResult<BranchDto>> ListAsync(PageRequest page, CancellationToken ct)
    {
        var scope = await access.GetBranchScopeAsync(Permissions.BranchesRead, ct);
        var q = db.Branches.AsNoTracking();
        if (!scope.AllBranches)
        {
            var ids = scope.BranchIds.ToList();
            q = q.Where(b => ids.Contains(b.Id));
        }
        if (page.Search is { } s) q = q.Where(b => b.Name.Contains(s) || b.Code.Contains(s));
        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(b => b.Code).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<BranchDto>(items.Select(ToDto).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<BranchDto> GetAsync(Guid id, CancellationToken ct) =>
        ToDto(await FindInScopeAsync(id, Permissions.BranchesRead, ct));

    public async Task<BranchDto> CreateAsync(CreateBranchRequest req, CancellationToken ct)
    {
        await access.EnsureTenantWideAsync(Permissions.BranchesManage, ct);
        Validate(req.Code, req.Name, req.Address, req.Phone, null, requireRowVersion: false);
        var code = req.Code!.Trim();
        if (await db.Branches.AnyAsync(b => b.Code == code, ct)) throw new ConflictException(ErrorCodes.BranchCodeTaken);
        var branch = new Branch { Code = code, Name = req.Name!.Trim(), Address = req.Address?.Trim(), Phone = req.Phone?.Trim(), IsActive = req.IsActive ?? true };
        db.Branches.Add(branch);
        await db.SaveChangesAsync(ct);
        return ToDto(branch);
    }

    public async Task<BranchDto> UpdateAsync(Guid id, UpdateBranchRequest req, CancellationToken ct)
    {
        Validate(req.Code, req.Name, req.Address, req.Phone, req.RowVersion, requireRowVersion: true);
        var branch = await FindInScopeAsync(id, Permissions.BranchesManage, ct);
        var code = req.Code!.Trim();
        if (code != branch.Code && await db.Branches.AnyAsync(b => b.Code == code && b.Id != id, ct)) throw new ConflictException(ErrorCodes.BranchCodeTaken);
        db.SetOriginalRowVersion(branch, Convert.FromBase64String(req.RowVersion!));
        branch.Code = code;
        branch.Name = req.Name!.Trim();
        branch.Address = req.Address?.Trim();
        branch.Phone = req.Phone?.Trim();
        branch.IsActive = req.IsActive ?? branch.IsActive;
        await db.SaveChangesAsync(ct);
        return ToDto(branch);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await access.EnsureTenantWideAsync(Permissions.BranchesManage, ct);
        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException();
        if (await peopleUsage.IsBranchReferencedAsync(id, ct) || await assignmentUsage.IsBranchReferencedAsync(id, ct) ||
            await academicUsage.IsBranchReferencedAsync(id, ct))
            throw new ConflictException(ErrorCodes.BranchInUse);
        db.Branches.Remove(branch);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Out-of-scope branches are reported as not found (no existence leak across sibling branches).</summary>
    private async Task<Branch> FindInScopeAsync(Guid id, string permission, CancellationToken ct)
    {
        var scope = await access.GetBranchScopeAsync(permission, ct);
        if (!scope.Includes(id)) throw new NotFoundException();
        return await db.Branches.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException();
    }

    private static void Validate(string? code, string? name, string? address, string? phone, string? rowVersion, bool requireRowVersion)
    {
        var v = new Validator().RequiredText("code", code, 20).RequiredText("name", name, 200).OptionalText("address", address, 500).Phone("phone", phone);
        if (requireRowVersion) v.RowVersion("rowVersion", rowVersion);
        v.ThrowIfInvalid();
    }

    public static BranchDto ToDto(Branch b) => new(b.Id, b.Code, b.Name, b.Address, b.Phone, b.IsActive, Convert.ToBase64String(b.RowVersion));
}

public sealed class OrganizationProvisioner(IOrganizationDbContext db) : IOrganizationProvisioner
{
    public async Task ProvisionAsync(Guid tenantId, string name, string defaultLocale, string timeZone, CancellationToken ct = default)
    {
        if (await db.CentralOffices.AnyAsync(ct)) return;
        db.CentralOffices.Add(new CentralOffice { TenantId = tenantId, Name = name, DefaultLocale = defaultLocale, TimeZone = timeZone });
        await db.SaveChangesAsync(ct);
    }
}

public sealed class BranchDirectory(IOrganizationDbContext db) : IBranchDirectory
{
    public Task<bool> ExistsAsync(Guid branchId, CancellationToken ct = default) => db.Branches.AnyAsync(b => b.Id == branchId, ct);

    public Task<string?> GetNameAsync(Guid branchId, CancellationToken ct = default) =>
        db.Branches.AsNoTracking().Where(b => b.Id == branchId).Select(b => (string?)b.Name).FirstOrDefaultAsync(ct);
}

public sealed class OrganizationSettings(IOrganizationDbContext db) : IOrganizationSettings
{
    public async Task<string> GetTimeZoneAsync(CancellationToken ct = default) =>
        await db.CentralOffices.AsNoTracking().Select(o => o.TimeZone).FirstOrDefaultAsync(ct) ?? "Asia/Tehran";

    public async Task<string> GetInstituteNameAsync(CancellationToken ct = default) =>
        await db.CentralOffices.AsNoTracking().Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "";
}
