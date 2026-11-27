using EEMP.Modules.Academic.Contracts;
using EEMP.Modules.Finance.Domain;
using EEMP.Modules.Organization.Contracts;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Finance.Application.Fmt;

namespace EEMP.Modules.Finance.Application;

public sealed class FinanceSetupService(IFinanceDbContext db, FinanceActor actor, FinanceAccess access, FinanceAuditor audit,
    IEnumerable<IPaymentGateway> gateways, IBranchDirectory branches, IEnrollmentDirectory academic)
{
    /// <summary>The tenant's settings row (created with defaults on first use).</summary>
    public async Task<FinanceSettings> LoadAsync(CancellationToken ct)
    {
        var row = await db.Settings.FirstOrDefaultAsync(ct);
        if (row is not null) return row;
        db.Settings.Add(row = new FinanceSettings());
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ResetTracking();
            row = await db.Settings.FirstAsync(ct);
        }
        return row;
    }

    public async Task<FinanceSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        await EnsureCanReadSetupAsync(ct);
        return ToDto(await LoadAsync(ct), await actor.CurrencyAsync(ct));
    }

    public async Task<FinanceSettingsDto> SaveSettingsAsync(FinanceSettingsRequest req, CancellationToken ct)
    {
        var v = new Validator();
        var lateType = ParseEnum<LateFeeType>(req.LateFeeType ?? "fixed", "lateFeeType", v);
        var vat = req.VatRatePercent ?? 0;
        v.When(vat < 0 || vat > 100 || decimal.Round(vat, 2) != vat, "vatRatePercent", Validator.InvalidValue);
        var sibling = req.SiblingDiscountPercent ?? 0;
        v.When(sibling < 0 || sibling > 100 || decimal.Round(sibling, 2) != sibling, "siblingDiscountPercent", Validator.InvalidValue);
        var lateValue = req.LateFeeValue ?? 0;
        v.When(lateValue < 0 || (lateType == LateFeeType.Percent && lateValue > 100) || decimal.Round(lateValue, 2) != lateValue, "lateFeeValue", Validator.InvalidValue);
        v.When(req.LateFeeGraceDays is < 0 or > 365, "lateFeeGraceDays", Validator.InvalidValue);
        v.When(req.VatEnabled && vat == 0, "vatRatePercent", Validator.Required);
        v.RowVersion("rowVersion", req.RowVersion);
        v.ThrowIfInvalid();
        var currency = await actor.CurrencyAsync(ct);
        if (lateType == LateFeeType.Fixed && !Money.HasValidScale(lateValue, currency))
            new Validator().Add("lateFeeValue", Validator.InvalidValue).ThrowIfInvalid();

        var row = await LoadAsync(ct);
        CheckRowVersion(row, req.RowVersion);
        row.VatEnabled = req.VatEnabled;
        row.VatRatePercent = vat;
        row.LateFeeEnabled = req.LateFeeEnabled;
        row.LateFeeType = lateType!.Value;
        row.LateFeeValue = lateValue;
        row.LateFeeGraceDays = req.LateFeeGraceDays ?? 0;
        row.SiblingDiscountPercent = sibling;
        row.BlockResultsOnOverdue = req.BlockResultsOnOverdue;
        row.OnlinePaymentsEnabled = req.OnlinePaymentsEnabled;
        audit.Write("settings.updated", "settings", row.Id,
            details: $"vat={row.VatEnabled}:{row.VatRatePercent};late={row.LateFeeEnabled}:{row.LateFeeType}:{row.LateFeeValue}:{row.LateFeeGraceDays};sibling={row.SiblingDiscountPercent};block={row.BlockResultsOnOverdue};online={row.OnlinePaymentsEnabled}");
        await db.SaveChangesAsync(ct);
        return ToDto(row, currency);
    }

    private FinanceSettingsDto ToDto(FinanceSettings s, string currency) => new(currency, s.VatEnabled, s.VatRatePercent, s.LateFeeEnabled,
        Camel(s.LateFeeType), s.LateFeeValue, s.LateFeeGraceDays, s.SiblingDiscountPercent, s.BlockResultsOnOverdue, s.OnlinePaymentsEnabled,
        gateways.Where(g => g.Enabled).Select(g => g.Name).ToList(), Rv(s.RowVersion));

    private async Task EnsureCanReadSetupAsync(CancellationToken ct)
    {
        if (await access.HasAnyAsync(Permissions.FinanceSetup, ct) || await access.HasAnyAsync(Permissions.FinanceRead, ct)) return;
        throw new ForbiddenException();
    }

    // Fee types --------------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<FeeTypeDto>> ListFeeTypesAsync(CancellationToken ct)
    {
        await EnsureCanReadSetupAsync(ct);
        return (await db.FeeTypes.AsNoTracking().OrderBy(t => t.Code).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<FeeTypeDto> SaveFeeTypeAsync(Guid? id, FeeTypeRequest req, CancellationToken ct)
    {
        var v = new Validator().RequiredText("code", req.Code, 30).RequiredText("name", req.Name, 150);
        if (id is not null) v.RowVersion("rowVersion", req.RowVersion);
        v.ThrowIfInvalid();
        var code = req.Code!.Trim().ToUpperInvariant();
        FeeType row;
        if (id is null) db.FeeTypes.Add(row = new FeeType());
        else
        {
            row = await db.FeeTypes.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException();
            CheckRowVersion(row, req.RowVersion);
        }
        if (await db.FeeTypes.AnyAsync(t => t.Code == code && t.Id != row.Id, ct)) throw new ConflictException(ErrorCodes.DuplicateValue, P(("field", "code")));
        row.Code = code;
        row.Name = req.Name!.Trim();
        row.Taxable = req.Taxable;
        row.IsActive = req.IsActive ?? true;
        audit.Write(id is null ? "feeType.created" : "feeType.updated", "feeType", row.Id, details: row.Code);
        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    public async Task DeleteFeeTypeAsync(Guid id, CancellationToken ct)
    {
        var row = await db.FeeTypes.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException();
        if (await db.FeeItems.AnyAsync(i => i.FeeTypeId == id, ct)) throw new ConflictException(FinanceErrors.FeeTypeInUse);
        db.FeeTypes.Remove(row);
        audit.Write("feeType.deleted", "feeType", row.Id, details: row.Code);
        await db.SaveChangesAsync(ct);
    }

    private static FeeTypeDto ToDto(FeeType t) => new(t.Id, t.Code, t.Name, t.Taxable, t.IsActive, Rv(t.RowVersion));

    // Fee items --------------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<FeeItemDto>> ListFeeItemsAsync(Guid? termId, Guid? feeTypeId, bool activeOnly, CancellationToken ct)
    {
        await EnsureCanReadSetupAsync(ct);
        var q = db.FeeItems.AsNoTracking();
        if (termId is not null) q = q.Where(i => i.TermId == termId || i.TermId == null);
        if (feeTypeId is not null) q = q.Where(i => i.FeeTypeId == feeTypeId);
        if (activeOnly) q = q.Where(i => i.IsActive);
        var items = await q.OrderBy(i => i.Name).ToListAsync(ct);
        var types = await db.FeeTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Code, ct);
        return items.Select(i => ToDto(i, types.GetValueOrDefault(i.FeeTypeId, ""))).ToList();
    }

    public async Task<FeeItemDto> SaveFeeItemAsync(Guid? id, FeeItemRequest req, CancellationToken ct)
    {
        var currency = await actor.CurrencyAsync(ct);
        var v = new Validator().RequiredText("name", req.Name, 150).When(req.FeeTypeId is null || req.FeeTypeId == Guid.Empty, "feeTypeId", Validator.Required);
        var amount = Amount(req.Amount, "amount", currency, v);
        if (id is not null) v.RowVersion("rowVersion", req.RowVersion);
        v.ThrowIfInvalid();
        var type = await db.FeeTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == req.FeeTypeId, ct);
        v.When(type is null, "feeTypeId", Validator.InvalidValue);
        if (req.BranchId is { } b && !await branches.ExistsAsync(b, ct)) v.Add("branchId", Validator.InvalidValue);
        if (req.TermId is { } term && !await academic.TermExistsAsync(term, ct)) v.Add("termId", Validator.InvalidValue);
        v.ThrowIfInvalid();

        FeeItem row;
        if (id is null) db.FeeItems.Add(row = new FeeItem { Currency = currency });
        else
        {
            row = await db.FeeItems.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException();
            CheckRowVersion(row, req.RowVersion);
            if (row.Currency != currency) throw new BusinessRuleException(FinanceErrors.CurrencyMismatch);
        }
        row.FeeTypeId = type!.Id;
        row.Name = req.Name!.Trim();
        row.Amount = amount!.Value;
        row.TermId = req.TermId;
        row.CourseId = req.CourseId;
        row.BranchId = req.BranchId;
        row.IsActive = req.IsActive ?? true;
        audit.Write(id is null ? "feeItem.created" : "feeItem.updated", "feeItem", row.Id, amount: row.Amount, currency: row.Currency, details: row.Name);
        await db.SaveChangesAsync(ct);
        return ToDto(row, type.Code);
    }

    public async Task DeleteFeeItemAsync(Guid id, CancellationToken ct)
    {
        var row = await db.FeeItems.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException();
        if (await db.InvoiceLines.AnyAsync(l => l.FeeItemId == id, ct)) throw new ConflictException(FinanceErrors.FeeItemInUse);
        db.FeeItems.Remove(row);
        audit.Write("feeItem.deleted", "feeItem", row.Id, amount: row.Amount, currency: row.Currency);
        await db.SaveChangesAsync(ct);
    }

    private static FeeItemDto ToDto(FeeItem i, string typeCode) => new(i.Id, i.FeeTypeId, typeCode, i.Name, i.Amount, i.Currency, i.TermId, i.CourseId,
        i.BranchId, i.IsActive, Rv(i.RowVersion));
}
