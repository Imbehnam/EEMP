using EEMP.Modules.Academic.Domain;
using EEMP.Modules.Organization.Contracts;
using EEMP.Modules.People.Contracts;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Academic.Application.Fmt;

namespace EEMP.Modules.Academic.Application;

/// <summary>
/// Everything a printed report card shows. Scores, totals, bands, student and term come only from the stored version
/// snapshot (<see cref="Content"/>); institute and branch names are display labels read at render time.
/// </summary>
public sealed record ReportCardDocument(Guid ReportCardId, int VersionNo, string VersionStatus, DateTime? PublishedAt, string InstituteName,
    string BranchName, string TimeZoneId, ReportCardContent Content);

/// <summary>Renders a report-card document to PDF bytes in one of fa / en / tr / ar.</summary>
public interface IReportCardPdfRenderer
{
    byte[] Render(ReportCardDocument document, string language, DateTime renderedAtUtc);
}

/// <summary>Loads report-card versions for PDF output with the same authorization as the JSON reads.</summary>
public sealed class ReportCardDocuments(IAcademicDbContext db, IAccessControl access, ClassAccess classAccess, AcademicActor actor,
    IOrganizationSettings settings, IBranchDirectory branches)
{
    /// <summary>Maximum cards in one class archive (keeps one request bounded).</summary>
    public const int MaxArchiveCards = 200;

    /// <summary>Admin: any version of a card in the caller's <c>reportcards.read</c> branch scope (else 404).</summary>
    public async Task<ReportCardDocument> VersionAsync(Guid id, int versionNo, CancellationToken ct)
    {
        var card = await db.ReportCards.AsNoTracking().Include(r => r.Versions).FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException();
        if (!(await access.GetBranchScopeAsync(Permissions.ReportCardsRead, ct)).Includes(card.BranchId)) throw new NotFoundException();
        var version = card.Versions.FirstOrDefault(v => v.VersionNo == versionNo) ?? throw new NotFoundException();
        return await DocumentAsync(card, version, ct);
    }

    /// <summary>Portal: the latest published version, only for the caller's own or linked student (else 404).</summary>
    public async Task<ReportCardDocument> PublishedForPortalAsync(Guid id, CancellationToken ct)
    {
        var card = await db.ReportCards.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException();
        if (!(await actor.PortalStudentIdsAsync(ct)).Contains(card.StudentId)) throw new NotFoundException();
        if (card.PublishedVersion is not { } published) throw new NotFoundException();
        var version = await db.ReportCardVersions.AsNoTracking().FirstAsync(v => v.ReportCardId == id && v.VersionNo == published, ct);
        return await DocumentAsync(card, version, ct);
    }

    /// <summary>
    /// Admin: the published version of every card of the class's students in the class's term, limited to the caller's
    /// <c>reportcards.read</c> branch scope. Unpublished cards are skipped.
    /// </summary>
    public async Task<IReadOnlyList<(ReportCardDocument Document, string StudentNumber)>> PublishedForClassAsync(Guid classId, CancellationToken ct)
    {
        var cls = await classAccess.AuthorizeAsync(classId, Permissions.ReportCardsRead, false, Permissions.ReportCardsRead, ct);
        var scope = await access.GetBranchScopeAsync(Permissions.ReportCardsRead, ct);
        var studentIds = await db.Enrollments.AsNoTracking().Where(e => e.ClassId == classId && e.Status != EnrollmentStatus.Withdrawn)
            .Select(e => e.StudentId).ToListAsync(ct);
        var cards = (await db.ReportCards.AsNoTracking().Include(r => r.Versions)
                .Where(r => r.TermId == cls.TermId && studentIds.Contains(r.StudentId) && r.PublishedVersion != null).ToListAsync(ct))
            .Where(r => scope.Includes(r.BranchId)).ToList();
        if (cards.Count > MaxArchiveCards) throw new BusinessRuleException(AcademicErrors.ArchiveTooLarge, P(("max", MaxArchiveCards)));
        var result = new List<(ReportCardDocument, string)>();
        foreach (var card in cards)
        {
            var version = card.Versions.First(v => v.VersionNo == card.PublishedVersion);
            var doc = await DocumentAsync(card, version, ct);
            result.Add((doc, doc.Content.Student.StudentNumber));
        }
        return result.OrderBy(r => r.Item2, StringComparer.Ordinal).ToList();
    }

    private string? _institute;
    private string? _timeZone;
    private readonly Dictionary<Guid, string> _branchNames = [];

    private async Task<ReportCardDocument> DocumentAsync(ReportCard card, ReportCardVersion version, CancellationToken ct)
    {
        _institute ??= await settings.GetInstituteNameAsync(ct);
        _timeZone ??= await settings.GetTimeZoneAsync(ct);
        if (!_branchNames.TryGetValue(card.BranchId, out var branch))
            _branchNames[card.BranchId] = branch = await branches.GetNameAsync(card.BranchId, ct) ?? "";
        return new ReportCardDocument(card.Id, version.VersionNo, Camel(version.Status), version.PublishedAt, _institute, branch, _timeZone,
            ReportCardService.Content(version));
    }
}
