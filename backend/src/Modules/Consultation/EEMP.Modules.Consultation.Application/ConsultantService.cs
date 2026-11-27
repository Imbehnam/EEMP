using EEMP.Modules.Consultation.Domain;
using EEMP.Modules.Identity.Contracts;
using EEMP.Modules.Organization.Contracts;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Consultation.Application.Fmt;

namespace EEMP.Modules.Consultation.Application;

/// <summary>Tenant booking policy. Anyone in the tenant may read it (the portal shows the cancellation window); a tenant-wide
/// <c>consultation.consultants.manage</c> grant changes it.</summary>
public sealed class SettingsService(IConsultationDbContext db, IAccessControl access)
{
    public static SettingsDto ToDto(ConsultationSettings? s) => s is null
        ? new SettingsDto(ConsultationSettings.DefaultCancellationWindowHours, ConsultationSettings.DefaultBookingHorizonDays,
            ConsultationSettings.DefaultMinLeadHours, ConsultationSettings.DefaultMaxActiveBookings, null)
        : new SettingsDto(s.CancellationWindowHours, s.BookingHorizonDays, s.MinLeadHours, s.MaxActiveBookingsPerStudent, Rv(s.RowVersion));

    public static async Task<ConsultationSettings> CurrentAsync(IConsultationDbContext db, CancellationToken ct) =>
        await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new ConsultationSettings();

    public async Task<SettingsDto> GetAsync(CancellationToken ct) => ToDto(await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct));

    public async Task<SettingsDto> UpdateAsync(UpdateSettingsRequest req, CancellationToken ct)
    {
        await access.EnsureTenantWideAsync(Permissions.ConsultationConsultantsManage, ct);
        var v = new Validator()
            .When(req.CancellationWindowHours is < 0 or > 336, "cancellationWindowHours", Validator.InvalidValue)
            .When(req.BookingHorizonDays is < 1 or > 180, "bookingHorizonDays", Validator.InvalidValue)
            .When(req.MinLeadHours is < 0 or > 168, "minLeadHours", Validator.InvalidValue)
            .When(req.MaxActiveBookingsPerStudent is < 1 or > 20, "maxActiveBookingsPerStudent", Validator.InvalidValue);
        var row = await db.Settings.FirstOrDefaultAsync(ct);
        if (row is not null) v.RowVersion("rowVersion", req.RowVersion);
        v.ThrowIfInvalid();
        if (row is null)
        {
            row = new ConsultationSettings();
            db.Settings.Add(row);
        }
        else db.SetOriginalRowVersion(row, Convert.FromBase64String(req.RowVersion!));
        row.CancellationWindowHours = req.CancellationWindowHours;
        row.BookingHorizonDays = req.BookingHorizonDays;
        row.MinLeadHours = req.MinLeadHours;
        row.MaxActiveBookingsPerStudent = req.MaxActiveBookingsPerStudent;
        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }
}

/// <summary>Consultant profiles (DL-091). Managed by holders of consultation.consultants.manage in every branch the profile serves.</summary>
public sealed class ConsultantService(IConsultationDbContext db, ConsultationActor actor, IUserDirectory users, IBranchDirectory branches, IAccessControl access)
{
    public const int MaxName = 200, MaxBio = 1000, MaxSpecialties = 500, MaxBranches = 20;

    public static ConsultantDto ToDto(Consultant c) => new(c.Id, c.UserId, c.DisplayName, c.Bio, c.Specialties, c.IsActive,
        c.Branches.Select(b => b.BranchId).OrderBy(x => x).ToList(), Rv(c.RowVersion));

    public async Task<ConsultationMeDto> MeAsync(CancellationToken ct)
    {
        var c = await actor.ConsultantAsync(ct);
        var consult = await actor.ScopeAsync(Permissions.ConsultationConsult, ct);
        return new ConsultationMeDto(c is null ? null : ToDto(c), c is not null && c.IsActive && !consult.IsEmpty,
            !(await actor.ScopeAsync(Permissions.ConsultationConsultantsManage, ct)).IsEmpty,
            !(await actor.ScopeAsync(Permissions.ConsultationBookingsRead, ct)).IsEmpty,
            !(await actor.ScopeAsync(Permissions.ConsultationBookingsManage, ct)).IsEmpty,
            !(await actor.ScopeAsync(Permissions.ConsultationNotesRead, ct)).IsEmpty,
            await access.HasTenantWideAsync(Permissions.ConsultationConsultantsManage, ct));
    }

    public async Task<IReadOnlyList<ConsultantDto>> ListAsync(Guid? branchId, bool includeInactive, CancellationToken ct)
    {
        var scope = await actor.AnyScopeAsync(ct, Permissions.ConsultationConsultantsManage, Permissions.ConsultationBookingsRead,
            Permissions.ConsultationBookingsManage);
        var mine = await actor.ConsultantAsync(ct);
        if (scope.IsEmpty && mine is null) throw new ForbiddenException();
        var rows = await db.Consultants.AsNoTracking().Include(c => c.Branches).OrderBy(c => c.DisplayName).ToListAsync(ct);
        return rows.Where(c => c.Id == mine?.Id || c.Branches.Any(b => scope.Includes(b.BranchId)))
            .Where(c => includeInactive || c.IsActive)
            .Where(c => branchId is null || c.Serves(branchId.Value))
            .Select(ToDto).ToList();
    }

    public async Task<ConsultantDto> GetAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Consultants.AsNoTracking().Include(x => x.Branches).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        var scope = await actor.AnyScopeAsync(ct, Permissions.ConsultationConsultantsManage, Permissions.ConsultationBookingsRead,
            Permissions.ConsultationBookingsManage);
        if (c.UserId != actor.UserId && !c.Branches.Any(b => scope.Includes(b.BranchId))) throw new NotFoundException();
        return ToDto(c);
    }

    public async Task<ConsultantDto> CreateAsync(CreateConsultantRequest req, CancellationToken ct)
    {
        var (name, bio, specialties, branchIds) = await ValidateAsync(req.DisplayName, req.Bio, req.Specialties, req.BranchIds, null, false, ct);
        if (!await users.ExistsAsync(req.UserId, ct))
            throw new ValidationException(new Dictionary<string, string[]> { ["userId"] = [ConsultationErrors.UserNotFound] });
        await EnsureManageAsync(branchIds, ct);
        if (await db.Consultants.AnyAsync(c => c.UserId == req.UserId, ct)) throw new ConflictException(ConsultationErrors.ConsultantExists);
        var c = new Consultant { UserId = req.UserId, DisplayName = name, Bio = bio, Specialties = specialties };
        foreach (var b in branchIds) c.Branches.Add(new ConsultantBranch { ConsultantId = c.Id, BranchId = b });
        db.Consultants.Add(c);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException) { throw new ConflictException(ConsultationErrors.ConsultantExists); }
        return ToDto(c);
    }

    public async Task<ConsultantDto> UpdateAsync(Guid id, UpdateConsultantRequest req, CancellationToken ct)
    {
        var c = await db.Consultants.Include(x => x.Branches).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        var manage = await actor.ScopeAsync(Permissions.ConsultationConsultantsManage, ct);
        if (!c.Branches.Any(b => manage.Includes(b.BranchId))) throw new NotFoundException();
        var (name, bio, specialties, branchIds) = await ValidateAsync(req.DisplayName, req.Bio, req.Specialties, req.BranchIds, req.RowVersion, true, ct);
        await EnsureManageAsync(c.Branches.Select(b => b.BranchId).Concat(branchIds).Distinct().ToList(), ct);
        db.SetOriginalRowVersion(c, Convert.FromBase64String(req.RowVersion!));
        c.DisplayName = name;
        c.Bio = bio;
        c.Specialties = specialties;
        c.IsActive = req.IsActive;
        var current = c.Branches.Select(b => b.BranchId).ToHashSet();
        foreach (var gone in c.Branches.Where(b => !branchIds.Contains(b.BranchId)).ToList()) c.Branches.Remove(gone);
        foreach (var added in branchIds.Where(b => !current.Contains(b))) c.Branches.Add(new ConsultantBranch { ConsultantId = c.Id, BranchId = added });
        await db.SaveChangesAsync(ct);
        return ToDto(c);
    }

    private async Task<(string Name, string? Bio, string? Specialties, List<Guid> BranchIds)> ValidateAsync(string? displayName, string? bio,
        string? specialties, IReadOnlyList<Guid>? branchIds, string? rowVersion, bool requireRowVersion, CancellationToken ct)
    {
        var name = Clean(displayName);
        bio = Clean(bio);
        specialties = Clean(specialties);
        var ids = (branchIds ?? []).Distinct().ToList();
        var v = new Validator().RequiredText("displayName", name, MaxName).OptionalText("bio", bio, MaxBio)
            .OptionalText("specialties", specialties, MaxSpecialties);
        if (ids.Count == 0) v.Add("branchIds", Validator.Required);
        else if (ids.Count > MaxBranches) v.Add("branchIds", Validator.InvalidValue);
        if (requireRowVersion) v.RowVersion("rowVersion", rowVersion);
        foreach (var b in ids)
            if (!await branches.ExistsAsync(b, ct)) { v.Add("branchIds", ConsultationErrors.BranchNotFound); break; }
        v.ThrowIfInvalid();
        return (name!, bio, specialties, ids);
    }

    private async Task EnsureManageAsync(IReadOnlyCollection<Guid> branchIds, CancellationToken ct)
    {
        foreach (var b in branchIds) await access.EnsureBranchAsync(Permissions.ConsultationConsultantsManage, b, ct);
    }
}
