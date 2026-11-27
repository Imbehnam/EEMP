using EEMP.Modules.Identity.Contracts;
using EEMP.Modules.Workflow.Contracts;
using EEMP.Modules.Workflow.Domain;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Tenancy;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Workflow.Application.WorkflowFormat;

namespace EEMP.Modules.Workflow.Application;

/// <summary>
/// Workflow definitions (tenant configuration). Versions are drafts until published; published versions are immutable and
/// publishing retires the previous one. Running instances keep the version they started on.
/// </summary>
public sealed class DefinitionService(IWorkflowDbContext db, IUserDirectory users, ITenantContext tenant, TimeProvider clock,
    IEnumerable<IWorkflowSubjectPolicy> policies)
{
    public const int MaxSteps = 10;
    public const int MaxSlaHours = 8760;

    public async Task<IReadOnlyList<DefinitionSummaryDto>> ListAsync(CancellationToken ct)
    {
        var defs = await db.Definitions.AsNoTracking().Include(d => d.Versions).OrderBy(d => d.Name).ToListAsync(ct);
        return defs.Select(Summary).ToList();
    }

    public async Task<DefinitionDto> GetAsync(Guid id, CancellationToken ct) => Detail(await LoadAsync(id, track: false, ct));

    public async Task<DefinitionDto> CreateAsync(CreateDefinitionRequest req, CancellationToken ct)
    {
        var v = new Validator().RequiredText("name", req.Name, 200);
        if (!IsIdentifier(req.Key)) v.Add("key", string.IsNullOrWhiteSpace(req.Key) ? Validator.Required : Validator.InvalidFormat);
        if (!IsIdentifier(req.SubjectModule)) v.Add("subjectModule", string.IsNullOrWhiteSpace(req.SubjectModule) ? Validator.Required : Validator.InvalidFormat);
        if (!IsIdentifier(req.SubjectType)) v.Add("subjectType", string.IsNullOrWhiteSpace(req.SubjectType) ? Validator.Required : Validator.InvalidFormat);
        var steps = await ValidateStepsAsync(req.Steps, v, ct);
        v.ThrowIfInvalid();
        if (await db.Definitions.AnyAsync(d => d.Key == req.Key, ct)) throw new ConflictException(WorkflowErrors.DefinitionKeyTaken);

        var def = new WorkflowDefinition { Key = req.Key!, Name = req.Name!.Trim(), SubjectModule = req.SubjectModule!, SubjectType = req.SubjectType! };
        var version = new WorkflowDefinitionVersion { DefinitionId = def.Id, VersionNo = 1, RequireDistinctApprovers = req.RequireDistinctApprovers ?? true };
        foreach (var s in steps) s.VersionId = version.Id;
        version.Steps.AddRange(steps);
        def.Versions.Add(version);
        db.Definitions.Add(def);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            if (!await KeyFreeAsync(req.Key!, ct)) throw new ConflictException(WorkflowErrors.DefinitionKeyTaken);
            throw;
        }
        return await GetAsync(def.Id, ct);
    }

    private async Task<bool> KeyFreeAsync(string key, CancellationToken ct)
    {
        db.ResetTracking();
        return !await db.Definitions.AnyAsync(d => d.Key == key, ct);
    }

    public async Task<DefinitionDto> UpdateAsync(Guid id, UpdateDefinitionRequest req, CancellationToken ct)
    {
        var v = new Validator().RequiredText("name", req.Name, 200).RowVersion("rowVersion", req.RowVersion)
            .When(req.IsActive is null, "isActive", Validator.Required);
        v.ThrowIfInvalid();
        var def = await LoadAsync(id, track: true, ct);
        db.SetOriginalRowVersion(def, Convert.FromBase64String(req.RowVersion!));
        def.Name = req.Name!.Trim();
        def.IsActive = req.IsActive!.Value;
        await db.SaveChangesAsync(ct);
        return Detail(def);
    }

    /// <summary>New draft copied from the latest version (one draft at a time).</summary>
    public async Task<DefinitionDto> NewVersionAsync(Guid id, CancellationToken ct)
    {
        var def = await LoadAsync(id, track: true, ct);
        if (def.Versions.Any(x => x.Status == DefinitionVersionStatus.Draft)) throw new ConflictException(WorkflowErrors.DraftExists);
        var latest = def.Versions.OrderByDescending(x => x.VersionNo).First();
        var version = new WorkflowDefinitionVersion { DefinitionId = def.Id, VersionNo = latest.VersionNo + 1, RequireDistinctApprovers = latest.RequireDistinctApprovers };
        version.Steps.AddRange(latest.Steps.OrderBy(s => s.Order).Select(s => new WorkflowStepDefinition
        {
            VersionId = version.Id, Order = s.Order, Name = s.Name, Permission = s.Permission, AssigneeUserId = s.AssigneeUserId,
            SlaHours = s.SlaHours, AllowReturn = s.AllowReturn,
        }));
        db.Versions.Add(version);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<DefinitionDto> UpdateVersionAsync(Guid id, int versionNo, UpdateVersionRequest req, CancellationToken ct)
    {
        var v = new Validator().RowVersion("rowVersion", req.RowVersion);
        var steps = await ValidateStepsAsync(req.Steps, v, ct);
        v.ThrowIfInvalid();
        var def = await LoadAsync(id, track: true, ct);
        var version = def.Versions.FirstOrDefault(x => x.VersionNo == versionNo) ?? throw new NotFoundException();
        if (version.Status != DefinitionVersionStatus.Draft) throw new ConflictException(WorkflowErrors.VersionNotDraft);
        db.SetOriginalRowVersion(version, Convert.FromBase64String(req.RowVersion!));
        version.RequireDistinctApprovers = req.RequireDistinctApprovers ?? version.RequireDistinctApprovers;
        db.Steps.RemoveRange(version.Steps);
        version.Steps.Clear();
        foreach (var s in steps) { s.VersionId = version.Id; db.Steps.Add(s); version.Steps.Add(s); }
        version.UpdatedAt = clock.GetUtcNow().UtcDateTime; // the version row must change so its rowversion is checked and bumped
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Publishes a draft (definition rowVersion); the previously published version is retired.</summary>
    public async Task<DefinitionDto> PublishAsync(Guid id, int versionNo, PublishVersionRequest req, CancellationToken ct)
    {
        new Validator().RowVersion("rowVersion", req.RowVersion).ThrowIfInvalid();
        var def = await LoadAsync(id, track: true, ct);
        var version = def.Versions.FirstOrDefault(x => x.VersionNo == versionNo) ?? throw new NotFoundException();
        if (version.Status != DefinitionVersionStatus.Draft)
            throw new ConflictException(WorkflowErrors.VersionNotDraft, P(("status", Camel(version.Status))));
        EnsureStepsAllowed(policies, def.SubjectModule, def.SubjectType, version.Steps.Select(x => x.Permission));
        db.SetOriginalRowVersion(def, Convert.FromBase64String(req.RowVersion!));
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var old in def.Versions.Where(x => x.Status == DefinitionVersionStatus.Published)) old.Status = DefinitionVersionStatus.Retired;
        version.Status = DefinitionVersionStatus.Published;
        version.PublishedAt = now;
        version.PublishedByUserId = tenant.UserId;
        def.PublishedVersion = versionNo;
        await db.SaveChangesAsync(ct);
        return Detail(def);
    }

    /// <summary>DL-083: a subject module may restrict which permissions its approval steps require (e.g. finance.approve only).</summary>
    internal static void EnsureStepsAllowed(IEnumerable<IWorkflowSubjectPolicy> policies, string subjectModule, string subjectType,
        IEnumerable<string> stepPermissions)
    {
        foreach (var policy in policies.Where(p => p.SubjectModule == subjectModule))
        {
            var allowed = policy.AllowedStepPermissions(subjectType);
            if (allowed.Count == 0) continue;
            var bad = stepPermissions.FirstOrDefault(p => !allowed.Contains(p));
            if (bad is not null)
                throw new BusinessRuleException(WorkflowErrors.StepPermissionNotAllowed, P(("permission", bad), ("allowed", string.Join(", ", allowed))));
        }
    }

    private async Task<List<WorkflowStepDefinition>> ValidateStepsAsync(List<StepRequest>? steps, Validator v, CancellationToken ct)
    {
        var result = new List<WorkflowStepDefinition>();
        if (steps is null || steps.Count == 0) { v.Add("steps", Validator.Required); return result; }
        if (steps.Count > MaxSteps) { v.Add("steps", Validator.InvalidValue); return result; }
        for (var i = 0; i < steps.Count; i++)
        {
            var s = steps[i] ?? new StepRequest(null, null, null, null, null);
            var f = $"steps[{i}]";
            v.RequiredText($"{f}.name", s.Name, 200);
            if (string.IsNullOrWhiteSpace(s.Permission)) v.Add($"{f}.permission", Validator.Required);
            else if (!Permissions.Exists(s.Permission)) v.Add($"{f}.permission", Validator.UnknownPermission);
            if (s.SlaHours is { } h && (h < 1 || h > MaxSlaHours)) v.Add($"{f}.slaHours", Validator.InvalidValue);
            if (s.AssigneeUserId is { } u && (u == Guid.Empty || !await users.ExistsAsync(u, ct))) v.Add($"{f}.assigneeUserId", WorkflowErrors.UnknownAssignee);
            result.Add(new WorkflowStepDefinition
            {
                Order = i + 1, Name = s.Name?.Trim() ?? string.Empty, Permission = s.Permission ?? string.Empty,
                AssigneeUserId = s.AssigneeUserId, SlaHours = s.SlaHours, AllowReturn = s.AllowReturn ?? true,
            });
        }
        return result;
    }

    private async Task<WorkflowDefinition> LoadAsync(Guid id, bool track, CancellationToken ct)
    {
        IQueryable<WorkflowDefinition> q = db.Definitions.Include(d => d.Versions).ThenInclude(x => x.Steps);
        if (!track) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException();
    }

    private static DefinitionSummaryDto Summary(WorkflowDefinition d) => new(d.Id, d.Key, d.Name, d.SubjectModule, d.SubjectType, d.IsActive,
        d.PublishedVersion, d.Versions.Count == 0 ? 0 : d.Versions.Max(x => x.VersionNo), Rv(d.RowVersion));

    private static DefinitionDto Detail(WorkflowDefinition d) => new(d.Id, d.Key, d.Name, d.SubjectModule, d.SubjectType, d.IsActive,
        d.PublishedVersion, d.Versions.Count == 0 ? 0 : d.Versions.Max(x => x.VersionNo), Rv(d.RowVersion),
        d.Versions.OrderBy(x => x.VersionNo).Select(x => new VersionDto(x.VersionNo, Camel(x.Status), x.RequireDistinctApprovers, x.PublishedAt,
            Rv(x.RowVersion), x.Steps.OrderBy(s => s.Order).Select(s => new StepDto(s.Order, s.Name, s.Permission, s.AssigneeUserId, s.SlaHours, s.AllowReturn)).ToList()))
        .ToList());
}
