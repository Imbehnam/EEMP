using System.Globalization;
using System.IO.Compression;
using EEMP.Infrastructure.Authorization;
using EEMP.Infrastructure.Pdf;
using EEMP.Infrastructure.Persistence;
using EEMP.Infrastructure.Web;
using EEMP.Modules.Academic.Application;
using EEMP.Modules.Academic.Contracts;
using EEMP.Modules.Academic.Domain;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EEMP.Modules.Academic.Infrastructure;

public static class AcademicModule
{
    public static IServiceCollection AddAcademicModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<AcademicDbContext>(AcademicDbContext.SchemaName);
        services.AddScoped<IAcademicDbContext>(sp => sp.GetRequiredService<AcademicDbContext>());
        services.AddScoped<AcademicActor>();
        services.AddScoped<ClassAccess>();
        services.AddScoped<CatalogService>();
        services.AddScoped<ClassService>();
        services.AddScoped<EnrollmentService>();
        services.AddScoped<AttendanceService>();
        services.AddScoped<AssessmentService>();
        services.AddScoped<ReportCardService>();
        services.AddScoped<PortalAcademicService>();
        services.AddScoped<ReportCardDocuments>();
        services.AddSingleton<IReportCardPdfRenderer, ReportCardPdfRenderer>();
        services.AddScoped<IAcademicUsage, AcademicUsage>();
        services.AddScoped<IEnrollmentDirectory, EnrollmentDirectory>();
        return services;
    }

    /// <summary>
    /// Endpoint gates are coarse: a permission code, or "tenant user" for operations an assigned teacher may also perform.
    /// Branch scope and teacher assignment are always checked again in the application services.
    /// </summary>
    public static IEndpointRouteBuilder MapAcademicEndpoints(this IEndpointRouteBuilder api)
    {
        MapCatalog(api);
        MapClasses(api);
        MapEnrollmentAndAttendance(api);
        MapGrades(api);
        MapReportCards(api);
        MapPortal(api);
        return api;
    }

    private static void MapCatalog(IEndpointRouteBuilder api)
    {
        var years = api.MapGroup("/academic-years").WithTags("Academic years");
        years.MapGet("/", async ([AsParameters] PagingQuery q, CatalogService s, CancellationToken ct) => Results.Ok(await s.ListYearsAsync(q.ToPageRequest(), ct)))
            .RequirePermission(Permissions.CatalogRead);
        years.MapPost("/", async (AcademicYearRequest req, CatalogService s, CancellationToken ct) =>
        {
            var r = await s.SaveYearAsync(null, req, ct);
            return Results.Created($"/api/v1/academic-years/{r.Id}", r);
        }).RequirePermission(Permissions.CatalogManage);
        years.MapGet("/{id:guid}", async (Guid id, CatalogService s, CancellationToken ct) => Results.Ok(await s.GetYearAsync(id, ct)))
            .RequirePermission(Permissions.CatalogRead);
        years.MapPut("/{id:guid}", async (Guid id, AcademicYearRequest req, CatalogService s, CancellationToken ct) => Results.Ok(await s.SaveYearAsync(id, req, ct)))
            .RequirePermission(Permissions.CatalogManage);
        years.MapDelete("/{id:guid}", async (Guid id, CatalogService s, CancellationToken ct) =>
        {
            await s.DeleteYearAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.CatalogManage);

        var terms = api.MapGroup("/terms").WithTags("Terms");
        terms.MapGet("/", async (Guid? academicYearId, CatalogService s, CancellationToken ct) => Results.Ok(await s.ListTermsAsync(academicYearId, ct)))
            .RequirePermission(Permissions.CatalogRead);
        terms.MapPost("/", async (TermRequest req, CatalogService s, CancellationToken ct) =>
        {
            var r = await s.SaveTermAsync(null, req, ct);
            return Results.Created($"/api/v1/terms/{r.Id}", r);
        }).RequirePermission(Permissions.CatalogManage);
        terms.MapGet("/{id:guid}", async (Guid id, CatalogService s, CancellationToken ct) => Results.Ok(await s.GetTermAsync(id, ct)))
            .RequirePermission(Permissions.CatalogRead);
        terms.MapPut("/{id:guid}", async (Guid id, TermRequest req, CatalogService s, CancellationToken ct) => Results.Ok(await s.SaveTermAsync(id, req, ct)))
            .RequirePermission(Permissions.CatalogManage);
        terms.MapDelete("/{id:guid}", async (Guid id, CatalogService s, CancellationToken ct) =>
        {
            await s.DeleteTermAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.CatalogManage);

        var subjects = api.MapGroup("/subjects").WithTags("Subjects");
        subjects.MapGet("/", async ([AsParameters] PagingQuery q, CatalogService s, CancellationToken ct) => Results.Ok(await s.ListSubjectsAsync(q.ToPageRequest(), ct)))
            .RequirePermission(Permissions.CatalogRead);
        subjects.MapPost("/", async (SubjectRequest req, CatalogService s, CancellationToken ct) =>
        {
            var r = await s.SaveSubjectAsync(null, req, ct);
            return Results.Created($"/api/v1/subjects/{r.Id}", r);
        }).RequirePermission(Permissions.CatalogManage);
        subjects.MapGet("/{id:guid}", async (Guid id, CatalogService s, CancellationToken ct) => Results.Ok(await s.GetSubjectAsync(id, ct)))
            .RequirePermission(Permissions.CatalogRead);
        subjects.MapPut("/{id:guid}", async (Guid id, SubjectRequest req, CatalogService s, CancellationToken ct) => Results.Ok(await s.SaveSubjectAsync(id, req, ct)))
            .RequirePermission(Permissions.CatalogManage);
        subjects.MapDelete("/{id:guid}", async (Guid id, CatalogService s, CancellationToken ct) =>
        {
            await s.DeleteSubjectAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.CatalogManage);

        var courses = api.MapGroup("/courses").WithTags("Courses");
        courses.MapGet("/", async ([AsParameters] PagingQuery q, Guid? subjectId, CatalogService s, CancellationToken ct) =>
            Results.Ok(await s.ListCoursesAsync(q.ToPageRequest(), subjectId, ct))).RequirePermission(Permissions.CatalogRead);
        courses.MapPost("/", async (CourseRequest req, CatalogService s, CancellationToken ct) =>
        {
            var r = await s.SaveCourseAsync(null, req, ct);
            return Results.Created($"/api/v1/courses/{r.Id}", r);
        }).RequirePermission(Permissions.CatalogManage);
        courses.MapGet("/{id:guid}", async (Guid id, CatalogService s, CancellationToken ct) => Results.Ok(await s.GetCourseAsync(id, ct)))
            .RequirePermission(Permissions.CatalogRead);
        courses.MapPut("/{id:guid}", async (Guid id, CourseRequest req, CatalogService s, CancellationToken ct) => Results.Ok(await s.SaveCourseAsync(id, req, ct)))
            .RequirePermission(Permissions.CatalogManage);
        courses.MapDelete("/{id:guid}", async (Guid id, CatalogService s, CancellationToken ct) =>
        {
            await s.DeleteCourseAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.CatalogManage);

        var schemes = api.MapGroup("/grading-schemes").WithTags("Grading schemes");
        schemes.MapGet("/", async (CatalogService s, CancellationToken ct) => Results.Ok(await s.ListSchemesAsync(ct))).RequirePermission(Permissions.CatalogRead);
        schemes.MapPost("/", async (GradingSchemeRequest req, CatalogService s, CancellationToken ct) =>
        {
            var r = await s.SaveSchemeAsync(null, req, ct);
            return Results.Created($"/api/v1/grading-schemes/{r.Id}", r);
        }).RequirePermission(Permissions.CatalogManage);
        schemes.MapGet("/{id:guid}", async (Guid id, CatalogService s, CancellationToken ct) => Results.Ok(await s.GetSchemeAsync(id, ct)))
            .RequirePermission(Permissions.CatalogRead);
        schemes.MapPut("/{id:guid}", async (Guid id, GradingSchemeRequest req, CatalogService s, CancellationToken ct) => Results.Ok(await s.SaveSchemeAsync(id, req, ct)))
            .RequirePermission(Permissions.CatalogManage);
        schemes.MapDelete("/{id:guid}", async (Guid id, CatalogService s, CancellationToken ct) =>
        {
            await s.DeleteSchemeAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.CatalogManage);
    }

    private static void MapClasses(IEndpointRouteBuilder api)
    {
        var classes = api.MapGroup("/classes").WithTags("Classes");
        classes.MapGet("/", async ([AsParameters] PagingQuery q, Guid? termId, Guid? branchId, Guid? courseId, Guid? teacherId, string? status,
                ClassService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(q.ToPageRequest(), new ClassListQuery(termId, branchId, courseId, teacherId, status), ct)))
            .RequirePermission(Permissions.ClassesRead);
        classes.MapPost("/", async (ClassRequest req, ClassService s, CancellationToken ct) =>
        {
            var r = await s.CreateAsync(req, ct);
            return Results.Created($"/api/v1/classes/{r.Id}", r);
        }).RequirePermission(Permissions.ClassesManage);
        classes.MapGet("/{id:guid}", async (Guid id, ClassService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct)))
            .RequireAuthorization(Policies.TenantUser);
        classes.MapPut("/{id:guid}", async (Guid id, ClassRequest req, ClassService s, CancellationToken ct) => Results.Ok(await s.UpdateAsync(id, req, ct)))
            .RequirePermission(Permissions.ClassesManage);
        classes.MapDelete("/{id:guid}", async (Guid id, ClassService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.ClassesManage);
        classes.MapPut("/{id:guid}/schedule", async (Guid id, ScheduleRequest req, ClassService s, CancellationToken ct) => Results.Ok(await s.SetScheduleAsync(id, req, ct)))
            .RequirePermission(Permissions.ClassesManage);
        classes.MapPut("/{id:guid}/teachers", async (Guid id, TeachersRequest req, ClassService s, CancellationToken ct) => Results.Ok(await s.SetTeachersAsync(id, req, ct)))
            .RequirePermission(Permissions.ClassesManage);
        classes.MapGet("/{id:guid}/roster", async (Guid id, bool? includeInactive, ClassService s, CancellationToken ct) =>
            Results.Ok(await s.RosterAsync(id, includeInactive ?? false, ct))).RequireAuthorization(Policies.TenantUser);
    }

    private static void MapEnrollmentAndAttendance(IEndpointRouteBuilder api)
    {
        var enrollments = api.MapGroup("/enrollments").WithTags("Enrollments");
        enrollments.MapGet("/", async ([AsParameters] PagingQuery q, Guid? classId, Guid? studentId, Guid? termId, string? status, EnrollmentService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(q.ToPageRequest(), new EnrollmentListQuery(classId, studentId, termId, status), ct)))
            .RequirePermission(Permissions.EnrollmentsRead);
        enrollments.MapPost("/", async (EnrollRequest req, EnrollmentService s, CancellationToken ct) =>
        {
            var r = await s.EnrollAsync(req, ct);
            return Results.Created($"/api/v1/enrollments/{r.Id}", r);
        }).RequirePermission(Permissions.EnrollmentsManage);
        enrollments.MapGet("/{id:guid}", async (Guid id, EnrollmentService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct)))
            .RequirePermission(Permissions.EnrollmentsRead);
        enrollments.MapPost("/{id:guid}/withdraw", async (Guid id, WithdrawRequest req, EnrollmentService s, CancellationToken ct) =>
            Results.Ok(await s.WithdrawAsync(id, req, ct))).RequirePermission(Permissions.EnrollmentsManage);

        var classes = api.MapGroup("/classes").WithTags("Attendance");
        classes.MapGet("/{id:guid}/attendance", async (Guid id, string? date, Guid? scheduleSlotId, AttendanceService s, CancellationToken ct) =>
            Results.Ok(await s.GetSheetAsync(id, date, scheduleSlotId, ct))).RequireAuthorization(Policies.TenantUser);
        classes.MapPut("/{id:guid}/attendance", async (Guid id, AttendanceBulkRequest req, AttendanceService s, CancellationToken ct) =>
            Results.Ok(await s.MarkAsync(id, req, ct))).RequireAuthorization(Policies.TenantUser);
        classes.MapGet("/{id:guid}/attendance/sessions", async (Guid id, string? from, string? to, AttendanceService s, CancellationToken ct) =>
            Results.Ok(await s.SessionsAsync(id, from, to, ct))).RequireAuthorization(Policies.TenantUser);
        api.MapGet("/attendance", async ([AsParameters] PagingQuery q, Guid? studentId, Guid? classId, string? from, string? to, string? status,
                AttendanceService s, CancellationToken ct) =>
            Results.Ok(await s.ListRecordsAsync(q.ToPageRequest(), new AttendanceListQuery(studentId, classId, from, to, status), ct)))
            .WithTags("Attendance").RequirePermission(Permissions.AttendanceRead);
    }

    private static void MapGrades(IEndpointRouteBuilder api)
    {
        var classes = api.MapGroup("/classes").WithTags("Assessments");
        classes.MapGet("/{id:guid}/assessments", async (Guid id, AssessmentService s, CancellationToken ct) => Results.Ok(await s.ListAsync(id, ct)))
            .RequireAuthorization(Policies.TenantUser);
        classes.MapPost("/{id:guid}/assessments", async (Guid id, AssessmentRequest req, AssessmentService s, CancellationToken ct) =>
        {
            var r = await s.CreateAsync(id, req, ct);
            return Results.Created($"/api/v1/assessments/{r.Id}", r);
        }).RequireAuthorization(Policies.TenantUser);
        classes.MapGet("/{id:guid}/gradebook", async (Guid id, AssessmentService s, CancellationToken ct) => Results.Ok(await s.GradebookAsync(id, ct)))
            .RequireAuthorization(Policies.TenantUser);

        var a = api.MapGroup("/assessments").WithTags("Assessments").RequireAuthorization(Policies.TenantUser);
        a.MapGet("/{id:guid}", async (Guid id, AssessmentService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct)));
        a.MapPut("/{id:guid}", async (Guid id, AssessmentRequest req, AssessmentService s, CancellationToken ct) => Results.Ok(await s.UpdateAsync(id, req, ct)));
        a.MapDelete("/{id:guid}", async (Guid id, AssessmentService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        });
        a.MapGet("/{id:guid}/results", async (Guid id, AssessmentService s, CancellationToken ct) => Results.Ok(await s.GetResultsAsync(id, ct)));
        a.MapPut("/{id:guid}/results", async (Guid id, ResultsRequest req, AssessmentService s, CancellationToken ct) => Results.Ok(await s.SaveResultsAsync(id, req, ct)));
        a.MapGet("/{id:guid}/history", async (Guid id, AssessmentService s, CancellationToken ct) => Results.Ok(await s.HistoryAsync(id, ct)));
        a.MapPost("/{id:guid}/submit", async (Guid id, TransitionRequest req, AssessmentService s, CancellationToken ct) =>
            Results.Ok(await s.TransitionAsync(id, GradeAuditAction.Submitted, req, ct)));
        a.MapPost("/{id:guid}/return", async (Guid id, TransitionRequest req, AssessmentService s, CancellationToken ct) =>
            Results.Ok(await s.TransitionAsync(id, GradeAuditAction.Returned, req, ct))).RequirePermission(Permissions.GradesApprove);
        a.MapPost("/{id:guid}/approve", async (Guid id, TransitionRequest req, AssessmentService s, CancellationToken ct) =>
            Results.Ok(await s.TransitionAsync(id, GradeAuditAction.Approved, req, ct))).RequirePermission(Permissions.GradesApprove);
        a.MapPost("/{id:guid}/publish", async (Guid id, TransitionRequest req, AssessmentService s, CancellationToken ct) =>
            Results.Ok(await s.TransitionAsync(id, GradeAuditAction.Published, req, ct))).RequirePermission(Permissions.GradesPublish);
        a.MapPost("/{id:guid}/reopen", async (Guid id, TransitionRequest req, AssessmentService s, CancellationToken ct) =>
            Results.Ok(await s.TransitionAsync(id, GradeAuditAction.Reopened, req, ct))).RequirePermission(Permissions.GradesApprove);
    }

    private static void MapReportCards(IEndpointRouteBuilder api)
    {
        var rc = api.MapGroup("/report-cards").WithTags("Report cards");
        rc.MapPost("/generate", async (GenerateReportCardsRequest req, ReportCardService s, CancellationToken ct) => Results.Ok(await s.GenerateAsync(req, ct)))
            .RequirePermission(Permissions.ReportCardsManage);
        rc.MapPost("/publish", async (PublishReportCardsRequest req, ReportCardService s, CancellationToken ct) => Results.Ok(await s.PublishBatchAsync(req, ct)))
            .RequirePermission(Permissions.ReportCardsPublish);
        rc.MapGet("/", async ([AsParameters] PagingQuery q, Guid? termId, Guid? classId, Guid? studentId, Guid? branchId, string? status,
                ReportCardService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(q.ToPageRequest(), new ReportCardListQuery(termId, classId, studentId, branchId, status), ct)))
            .RequirePermission(Permissions.ReportCardsRead);
        rc.MapGet("/{id:guid}", async (Guid id, ReportCardService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct)))
            .RequirePermission(Permissions.ReportCardsRead);
        rc.MapGet("/{id:guid}/versions/{versionNo:int}", async (Guid id, int versionNo, ReportCardService s, CancellationToken ct) =>
            Results.Ok(await s.GetVersionAsync(id, versionNo, ct))).RequirePermission(Permissions.ReportCardsRead);
        rc.MapPost("/{id:guid}/publish", async (Guid id, TransitionRequest req, ReportCardService s, CancellationToken ct) =>
            Results.Ok(await s.PublishAsync(id, req, ct))).RequirePermission(Permissions.ReportCardsPublish);

        // PDF export (DL-057): rendered on the server from the stored version snapshot only.
        rc.MapGet("/{id:guid}/versions/{versionNo:int}/pdf", async (Guid id, int versionNo, string? lang, ReportCardDocuments docs,
                IReportCardPdfRenderer renderer, TimeProvider clock, HttpContext http, CancellationToken ct) =>
            {
                var locale = ResolveLocale(lang);
                var doc = await docs.VersionAsync(id, versionNo, ct);
                return Pdf(http, renderer.Render(doc, locale.Language, clock.GetUtcNow().UtcDateTime), FileName(doc));
            }).RequirePermission(Permissions.ReportCardsRead).Produces(StatusCodes.Status200OK, contentType: "application/pdf");
        rc.MapGet("/pdf-archive", async (Guid? classId, string? lang, ReportCardDocuments docs, IReportCardPdfRenderer renderer, TimeProvider clock,
                HttpContext http, CancellationToken ct) =>
            {
                new Validator().When(classId is null || classId == Guid.Empty, "classId", Validator.Required).ThrowIfInvalid();
                var locale = ResolveLocale(lang);
                var items = await docs.PublishedForClassAsync(classId!.Value, ct);
                var now = clock.GetUtcNow().UtcDateTime;
                using var buffer = new MemoryStream();
                using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (doc, studentNumber) in items)
                    {
                        var name = SafeSegment(studentNumber) is { Length: > 0 } safe ? $"{safe}-v{doc.VersionNo}.pdf" : FileName(doc);
                        if (!used.Add(name)) name = FileName(doc);
                        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
                        await using var stream = entry.Open();
                        var bytes = renderer.Render(doc, locale.Language, now);
                        await stream.WriteAsync(bytes, ct);
                    }
                }
                http.Response.Headers.CacheControl = "no-store";
                return Results.File(buffer.ToArray(), "application/zip", $"report-cards-{classId.Value.ToString("N")[..8]}.zip");
            }).RequirePermission(Permissions.ReportCardsRead).Produces(StatusCodes.Status200OK, contentType: "application/zip");
    }

    /// <summary><c>lang</c> query (fa-IR, fa, en, tr, ar) wins; otherwise the request culture from Accept-Language (default fa-IR).</summary>
    private static PdfLocale ResolveLocale(string? lang)
    {
        if (!string.IsNullOrWhiteSpace(lang))
        {
            var requested = PdfLocale.TryResolve(lang);
            if (requested is null) new Validator().Add("lang", Validator.UnsupportedLocale).ThrowIfInvalid();
            return requested!;
        }
        return PdfLocale.TryResolve(CultureInfo.CurrentUICulture.Name) ?? PdfLocale.TryResolve(SupportedLocales.Default)!;
    }

    /// <summary>No personal data in the file name: card id prefix and version only.</summary>
    private static string FileName(ReportCardDocument doc) => $"report-card-{doc.ReportCardId.ToString("N")[..8]}-v{doc.VersionNo}.pdf";

    private static string SafeSegment(string value) =>
        new string(value.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_').Take(40).ToArray());

    private static IResult Pdf(HttpContext http, byte[] bytes, string fileName)
    {
        http.Response.Headers.CacheControl = "no-store";
        return Results.File(bytes, "application/pdf", fileName);
    }

    private static void MapPortal(IEndpointRouteBuilder api)
    {
        // Any authenticated tenant user; the teacher assignment / guardian link / own profile is the authorization.
        var portal = api.MapGroup("/portal/me").WithTags("Portal").RequireAuthorization(Policies.TenantUser);
        portal.MapGet("/classes", async (Guid? termId, ClassService s, CancellationToken ct) => Results.Ok(await s.MyClassesAsync(termId, ct)));
        portal.MapGet("/students/{studentId:guid}/timetable", async (Guid studentId, Guid? termId, PortalAcademicService s, CancellationToken ct) =>
            Results.Ok(await s.TimetableAsync(studentId, termId, ct)));
        portal.MapGet("/students/{studentId:guid}/attendance", async (Guid studentId, Guid? termId, string? from, string? to, PortalAcademicService s, CancellationToken ct) =>
            Results.Ok(await s.AttendanceAsync(studentId, termId, from, to, ct)));
        portal.MapGet("/students/{studentId:guid}/grades", async (Guid studentId, Guid? termId, PortalAcademicService s, CancellationToken ct) =>
            Results.Ok(await s.GradesAsync(studentId, termId, ct)));
        portal.MapGet("/students/{studentId:guid}/report-cards", async (Guid studentId, PortalAcademicService s, CancellationToken ct) =>
            Results.Ok(await s.ReportCardsAsync(studentId, ct)));
        portal.MapGet("/report-cards/{id:guid}", async (Guid id, PortalAcademicService s, CancellationToken ct) => Results.Ok(await s.ReportCardAsync(id, ct)));
        portal.MapGet("/report-cards/{id:guid}/pdf", async (Guid id, string? lang, ReportCardDocuments docs, IReportCardPdfRenderer renderer,
                TimeProvider clock, HttpContext http, CancellationToken ct) =>
            {
                var locale = ResolveLocale(lang);
                var doc = await docs.PublishedForPortalAsync(id, ct);
                return Pdf(http, renderer.Render(doc, locale.Language, clock.GetUtcNow().UtcDateTime), FileName(doc));
            }).Produces(StatusCodes.Status200OK, contentType: "application/pdf");
    }
}
