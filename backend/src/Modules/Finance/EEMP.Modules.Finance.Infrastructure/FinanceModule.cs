using System.Globalization;
using EEMP.Infrastructure.Authorization;
using EEMP.Infrastructure.Pdf;
using EEMP.Infrastructure.Persistence;
using EEMP.Infrastructure.Web;
using EEMP.Modules.Finance.Application;
using EEMP.Modules.Finance.Contracts;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EEMP.Modules.Finance.Infrastructure;

public static class FinanceModule
{
    public static IServiceCollection AddFinanceModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<FinanceDbContext>(FinanceDbContext.SchemaName);
        services.AddScoped<IFinanceDbContext>(sp => sp.GetRequiredService<FinanceDbContext>());
        services.AddOptions<FinanceGatewayOptions>().Bind(configuration.GetSection(FinanceGatewayOptions.SectionName));
        services.AddOptions<FinancePaymentOptions>().Bind(configuration.GetSection(FinancePaymentOptions.SectionName));
        services.AddScoped<FinanceActor>();
        services.AddScoped<FinanceAccess>();
        services.AddScoped<FinanceAuditor>();
        services.AddScoped<DocumentNumberer>();
        services.AddScoped<FinanceUnitOfWork>();
        services.AddScoped<StudentNames>();
        services.AddScoped<InvoiceMapper>();
        services.AddScoped<PaymentMapper>();
        services.AddScoped<PaymentPosting>();
        services.AddScoped<IFinanceApprovals, LocalMakerChecker>();
        services.AddScoped<FinanceSetupService>();
        services.AddScoped<InvoiceService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<ApprovalService>();
        services.AddScoped<OnlinePaymentService>();
        services.AddScoped<PortalFinanceService>();
        services.AddScoped<FinanceReportService>();
        services.AddScoped<FinanceDocuments>();
        services.AddScoped<FinanceStanding>();
        services.AddScoped<IFinanceStanding>(sp => sp.GetRequiredService<FinanceStanding>());
        services.AddScoped<IFinanceUsage>(sp => sp.GetRequiredService<FinanceStanding>());
        services.AddSingleton<IFinancePdfRenderer, FinancePdfRenderer>();
        services.AddSingleton<FakePaymentGateway>();
        services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<FakePaymentGateway>());
        services.AddHttpClient<ZarinpalGateway>();
        services.AddScoped<IPaymentGateway>(sp => sp.GetRequiredService<ZarinpalGateway>());
        return services;
    }

    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder api)
    {
        MapSetup(api);
        MapInvoices(api);
        MapPayments(api);
        MapReports(api);
        MapPortal(api);
        MapGateway(api);
        return api;
    }

    private static void MapSetup(IEndpointRouteBuilder api)
    {
        var f = api.MapGroup("/finance").WithTags("Finance setup");
        f.MapGet("/settings", async (FinanceSetupService s, CancellationToken ct) => Results.Ok(await s.GetSettingsAsync(ct))).RequireAuthorization(Policies.TenantUser);
        f.MapPut("/settings", async (FinanceSettingsRequest req, FinanceSetupService s, CancellationToken ct) => Results.Ok(await s.SaveSettingsAsync(req, ct)))
            .RequirePermission(Permissions.FinanceSetup);

        f.MapGet("/fee-types", async (FinanceSetupService s, CancellationToken ct) => Results.Ok(await s.ListFeeTypesAsync(ct))).RequireAuthorization(Policies.TenantUser);
        f.MapPost("/fee-types", async (FeeTypeRequest req, FinanceSetupService s, CancellationToken ct) =>
        {
            var r = await s.SaveFeeTypeAsync(null, req, ct);
            return Results.Created($"/api/v1/finance/fee-types/{r.Id}", r);
        }).RequirePermission(Permissions.FinanceSetup);
        f.MapPut("/fee-types/{id:guid}", async (Guid id, FeeTypeRequest req, FinanceSetupService s, CancellationToken ct) => Results.Ok(await s.SaveFeeTypeAsync(id, req, ct)))
            .RequirePermission(Permissions.FinanceSetup);
        f.MapDelete("/fee-types/{id:guid}", async (Guid id, FinanceSetupService s, CancellationToken ct) =>
        {
            await s.DeleteFeeTypeAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.FinanceSetup);

        f.MapGet("/fee-items", async (Guid? termId, Guid? feeTypeId, bool? activeOnly, FinanceSetupService s, CancellationToken ct) =>
            Results.Ok(await s.ListFeeItemsAsync(termId, feeTypeId, activeOnly ?? false, ct))).RequireAuthorization(Policies.TenantUser);
        f.MapPost("/fee-items", async (FeeItemRequest req, FinanceSetupService s, CancellationToken ct) =>
        {
            var r = await s.SaveFeeItemAsync(null, req, ct);
            return Results.Created($"/api/v1/finance/fee-items/{r.Id}", r);
        }).RequirePermission(Permissions.FinanceSetup);
        f.MapPut("/fee-items/{id:guid}", async (Guid id, FeeItemRequest req, FinanceSetupService s, CancellationToken ct) => Results.Ok(await s.SaveFeeItemAsync(id, req, ct)))
            .RequirePermission(Permissions.FinanceSetup);
        f.MapDelete("/fee-items/{id:guid}", async (Guid id, FinanceSetupService s, CancellationToken ct) =>
        {
            await s.DeleteFeeItemAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.FinanceSetup);
    }

    private static void MapInvoices(IEndpointRouteBuilder api)
    {
        var inv = api.MapGroup("/finance/invoices").WithTags("Finance invoices");
        inv.MapGet("/", async ([AsParameters] PagingQuery q, string? status, Guid? studentId, Guid? branchId, Guid? termId, bool? overdueOnly, InvoiceService s,
                CancellationToken ct) => Results.Ok(await s.ListAsync(q.ToPageRequest(), new InvoiceListQuery(status, studentId, branchId, termId, overdueOnly), ct)))
            .RequirePermission(Permissions.FinanceRead);
        inv.MapPost("/", async (InvoiceRequest req, InvoiceService s, CancellationToken ct) =>
        {
            var r = await s.CreateAsync(req, ct);
            return Results.Created($"/api/v1/finance/invoices/{r.Id}", r);
        }).RequirePermission(Permissions.FinanceInvoicesManage);
        inv.MapGet("/{id:guid}", async (Guid id, InvoiceService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct))).RequirePermission(Permissions.FinanceRead);
        inv.MapPut("/{id:guid}", async (Guid id, InvoiceRequest req, InvoiceService s, CancellationToken ct) => Results.Ok(await s.UpdateAsync(id, req, ct)))
            .RequirePermission(Permissions.FinanceInvoicesManage);
        inv.MapDelete("/{id:guid}", async (Guid id, InvoiceService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.FinanceInvoicesManage);
        inv.MapPost("/{id:guid}/issue", async (Guid id, IssueRequest req, InvoiceService s, CancellationToken ct) => Results.Ok(await s.IssueAsync(id, req, ct)))
            .RequirePermission(Permissions.FinanceInvoicesManage);
        inv.MapPost("/{id:guid}/void", async (Guid id, VoidRequest req, InvoiceService s, CancellationToken ct) => Results.Ok(await s.VoidAsync(id, req, ct)))
            .RequirePermission(Permissions.FinanceInvoicesManage);
        inv.MapPost("/{id:guid}/credit-notes", async (Guid id, CreditNoteRequest req, InvoiceService s, CancellationToken ct) => Results.Ok(await s.CreditNoteAsync(id, req, ct)))
            .RequirePermission(Permissions.FinanceInvoicesManage);
        inv.MapPut("/{id:guid}/installments", async (Guid id, InstallmentsRequest req, InvoiceService s, CancellationToken ct) => Results.Ok(await s.SetInstallmentsAsync(id, req, ct)))
            .RequirePermission(Permissions.FinanceInvoicesManage);
        inv.MapPost("/{id:guid}/discounts", async (Guid id, DiscountRequest req, InvoiceService s, CancellationToken ct) =>
        {
            var r = await s.RequestDiscountAsync(id, req, ct);
            return Results.Created($"/api/v1/finance/discounts/{r.Id}", r);
        }).RequirePermission(Permissions.FinanceInvoicesManage);
        inv.MapPost("/generate", async (GenerateRequest req, InvoiceService s, CancellationToken ct) => Results.Ok(await s.GenerateAsync(req, ct)))
            .RequirePermission(Permissions.FinanceInvoicesManage);
        inv.MapPost("/generate-bulk", async (GenerateBulkRequest req, InvoiceService s, CancellationToken ct) => Results.Ok(await s.GenerateBulkAsync(req, ct)))
            .RequirePermission(Permissions.FinanceInvoicesManage);
        inv.MapGet("/{id:guid}/pdf", async (Guid id, string? lang, FinanceDocuments docs, IFinancePdfRenderer renderer, TimeProvider clock, HttpContext http,
                CancellationToken ct) =>
            {
                var locale = ResolveLocale(lang);
                var doc = await docs.InvoiceAsync(id, false, ct);
                return Pdf(http, renderer.RenderInvoice(doc, locale.Language, clock.GetUtcNow().UtcDateTime), $"invoice-{doc.Invoice.Number}.pdf");
            }).RequirePermission(Permissions.FinanceRead).Produces(StatusCodes.Status200OK, contentType: "application/pdf");

        var f = api.MapGroup("/finance").WithTags("Finance approvals");
        f.MapPost("/late-fees/apply", async (LateFeeRequest req, InvoiceService s, CancellationToken ct) => Results.Ok(await s.ApplyLateFeesAsync(req, ct)))
            .RequirePermission(Permissions.FinanceInvoicesManage);
        f.MapGet("/approvals", async (string? status, ApprovalService s, CancellationToken ct) => Results.Ok(await s.ListAsync(status, ct)))
            .RequirePermission(Permissions.FinanceRead);
        f.MapPost("/discounts/{id:guid}/approve", async (Guid id, DecisionRequest req, ApprovalService s, CancellationToken ct) =>
            Results.Ok(await s.DecideDiscountAsync(id, true, req, ct))).RequirePermission(Permissions.FinanceApprove);
        f.MapPost("/discounts/{id:guid}/reject", async (Guid id, DecisionRequest req, ApprovalService s, CancellationToken ct) =>
            Results.Ok(await s.DecideDiscountAsync(id, false, req, ct))).RequirePermission(Permissions.FinanceApprove);
        f.MapDelete("/discounts/{id:guid}", async (Guid id, ApprovalService s, CancellationToken ct) =>
        {
            await s.CancelDiscountAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.FinanceRead);
        f.MapPost("/refunds/{id:guid}/approve", async (Guid id, DecisionRequest req, ApprovalService s, CancellationToken ct) =>
            Results.Ok(await s.DecideRefundAsync(id, true, req, ct))).RequirePermission(Permissions.FinanceApprove);
        f.MapPost("/refunds/{id:guid}/reject", async (Guid id, DecisionRequest req, ApprovalService s, CancellationToken ct) =>
            Results.Ok(await s.DecideRefundAsync(id, false, req, ct))).RequirePermission(Permissions.FinanceApprove);
        f.MapGet("/refunds", async (string? status, PaymentService s, CancellationToken ct) => Results.Ok(await s.ListRefundsAsync(status, ct)))
            .RequirePermission(Permissions.FinanceRead);
    }

    private static void MapPayments(IEndpointRouteBuilder api)
    {
        var p = api.MapGroup("/finance/payments").WithTags("Finance payments");
        p.MapGet("/", async ([AsParameters] PagingQuery q, Guid? studentId, string? method, string? status, string? from, string? to, Guid? branchId,
                PaymentService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(q.ToPageRequest(), new PaymentListQuery(studentId, method, status, from, to, branchId), ct)))
            .RequirePermission(Permissions.FinanceRead);
        p.MapPost("/", async (PaymentRequest req, PaymentService s, CancellationToken ct) =>
        {
            var (r, created) = await s.RecordAsync(req, ct);
            return created ? Results.Created($"/api/v1/finance/payments/{r.Id}", r) : Results.Ok(r);
        }).RequirePermission(Permissions.FinancePaymentsManage);
        p.MapGet("/{id:guid}", async (Guid id, PaymentService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct))).RequirePermission(Permissions.FinanceRead);
        p.MapPost("/{id:guid}/cheque-status", async (Guid id, ChequeStatusRequest req, PaymentService s, CancellationToken ct) =>
            Results.Ok(await s.SetChequeStatusAsync(id, req, ct))).RequirePermission(Permissions.FinancePaymentsManage);
        p.MapPost("/{id:guid}/refunds", async (Guid id, RefundRequest req, PaymentService s, CancellationToken ct) =>
        {
            var r = await s.RequestRefundAsync(id, req, ct);
            return Results.Created($"/api/v1/finance/refunds/{r.Id}", r);
        }).RequirePermission(Permissions.FinancePaymentsManage);
        p.MapGet("/{id:guid}/receipt/pdf", async (Guid id, string? lang, FinanceDocuments docs, IFinancePdfRenderer renderer, TimeProvider clock, HttpContext http,
                CancellationToken ct) =>
            {
                var locale = ResolveLocale(lang);
                var doc = await docs.ReceiptAsync(id, false, ct);
                return Pdf(http, renderer.RenderReceipt(doc, locale.Language, clock.GetUtcNow().UtcDateTime), $"receipt-{doc.Payment.ReceiptNumber}.pdf");
            }).RequirePermission(Permissions.FinanceRead).Produces(StatusCodes.Status200OK, contentType: "application/pdf");

        var st = api.MapGroup("/finance/students").WithTags("Finance payments");
        st.MapGet("/{studentId:guid}/account", async (Guid studentId, PaymentService s, CancellationToken ct) => Results.Ok(await s.AccountAsync(studentId, ct)))
            .RequirePermission(Permissions.FinanceRead);
        st.MapPost("/{studentId:guid}/apply-credit", async (Guid studentId, ApplyCreditRequest req, PaymentService s, InvoiceMapper mapper, CancellationToken ct) =>
            Results.Ok(await s.ApplyCreditAsync(studentId, req, mapper, ct))).RequirePermission(Permissions.FinanceInvoicesManage);
    }

    private static void MapReports(IEndpointRouteBuilder api)
    {
        var r = api.MapGroup("/finance/reports").WithTags("Finance reports");
        r.MapGet("/outstanding", async (Guid? branchId, Guid? termId, string? format, FinanceReportService s, HttpContext http, CancellationToken ct) =>
        {
            var rows = await s.OutstandingAsync(branchId, termId, ct);
            if (!IsCsv(format)) return Results.Ok(rows);
            return CsvFile(http, "outstanding.csv", ["invoiceNumber", "studentNumber", "studentName", "branchId", "dueDate", "total", "paid", "balance", "overdue"],
                rows.Select(x => (IReadOnlyList<object?>)[x.Number, x.StudentNumber, x.StudentName, x.BranchId, x.DueDate, x.Total, x.Paid, x.Balance, x.IsOverdue]));
        }).RequirePermission(Permissions.FinanceReports);
        r.MapGet("/aging", async (string? asOf, Guid? branchId, string? format, FinanceReportService s, HttpContext http, CancellationToken ct) =>
        {
            var report = await s.AgingAsync(asOf, branchId, ct);
            if (!IsCsv(format)) return Results.Ok(report);
            return CsvFile(http, "aging.csv", ["studentNumber", "studentName", "branchId", "current", "d1_30", "d31_60", "d61_90", "d90plus", "total"],
                report.Rows.Select(x => (IReadOnlyList<object?>)[x.StudentNumber, x.StudentName, x.BranchId, x.Buckets.Current, x.Buckets.D1_30, x.Buckets.D31_60,
                    x.Buckets.D61_90, x.Buckets.D90plus, x.Buckets.Total]));
        }).RequirePermission(Permissions.FinanceReports);
        r.MapGet("/collections", async (string? from, string? to, Guid? branchId, string? format, FinanceReportService s, HttpContext http, CancellationToken ct) =>
        {
            var rows = await s.CollectionsAsync(from, to, branchId, ct);
            if (!IsCsv(format)) return Results.Ok(rows);
            return CsvFile(http, "collections.csv", ["date", "branchId", "method", "count", "amount"],
                rows.Select(x => (IReadOnlyList<object?>)[x.Date, x.BranchId, x.Method, x.Count, x.Amount]));
        }).RequirePermission(Permissions.FinanceReports);
    }

    private static void MapPortal(IEndpointRouteBuilder api)
    {
        var portal = api.MapGroup("/portal/me/finance").WithTags("Portal finance").RequireAuthorization(Policies.TenantUser);
        portal.MapGet("/students/{studentId:guid}/account", async (Guid studentId, PortalFinanceService s, CancellationToken ct) =>
            Results.Ok(await s.AccountAsync(studentId, ct)));
        portal.MapGet("/invoices/{id:guid}", async (Guid id, PortalFinanceService s, CancellationToken ct) => Results.Ok(await s.InvoiceAsync(id, ct)));
        portal.MapGet("/invoices/{id:guid}/pdf", async (Guid id, string? lang, FinanceDocuments docs, IFinancePdfRenderer renderer, TimeProvider clock,
                HttpContext http, CancellationToken ct) =>
            {
                var locale = ResolveLocale(lang);
                var doc = await docs.InvoiceAsync(id, true, ct);
                return Pdf(http, renderer.RenderInvoice(doc, locale.Language, clock.GetUtcNow().UtcDateTime), $"invoice-{doc.Invoice.Number}.pdf");
            }).Produces(StatusCodes.Status200OK, contentType: "application/pdf");
        portal.MapPost("/invoices/{id:guid}/pay", async (Guid id, PayOnlineRequest req, OnlinePaymentService s, IOptions<FinancePaymentOptions> options,
                HttpContext http, CancellationToken ct) =>
            Results.Ok(await s.InitiateAsync(id, req, CallbackBase(options.Value, http), ct)));
        portal.MapGet("/payments/{id:guid}", async (Guid id, PortalFinanceService s, CancellationToken ct) => Results.Ok(await s.PaymentAsync(id, ct)));
        portal.MapPost("/payments/{id:guid}/verify", async (Guid id, OnlinePaymentService s, CancellationToken ct) => Results.Ok(await s.VerifyAsync(id, ct)));
        portal.MapGet("/payments/{id:guid}/receipt/pdf", async (Guid id, string? lang, FinanceDocuments docs, IFinancePdfRenderer renderer, TimeProvider clock,
                HttpContext http, CancellationToken ct) =>
            {
                var locale = ResolveLocale(lang);
                var doc = await docs.ReceiptAsync(id, true, ct);
                return Pdf(http, renderer.RenderReceipt(doc, locale.Language, clock.GetUtcNow().UtcDateTime), $"receipt-{doc.Payment.ReceiptNumber}.pdf");
            }).Produces(StatusCodes.Status200OK, contentType: "application/pdf");
    }

    /// <summary>Anonymous provider callback (GET redirect or POST). Bound to the payment id and stored authority; never trusts query amounts.</summary>
    private static void MapGateway(IEndpointRouteBuilder api)
    {
        async Task<IResult> Handle(string provider, Guid paymentId, HttpContext http, OnlinePaymentService s, IOptions<FinancePaymentOptions> options, CancellationToken ct)
        {
            var form = http.Request.HasFormContentType ? await http.Request.ReadFormAsync(ct) : null;
            string? Read(string key) => http.Request.Query.TryGetValue(key, out var q) && q.Count > 0 ? q[0]
                : form is not null && form.TryGetValue(key, out var f) && f.Count > 0 ? f[0] : null;
            var result = await s.CallbackAsync(provider, paymentId, Read("Authority") ?? Read("authority"), Read("Status") ?? Read("status"), ct);
            http.Response.Headers.CacheControl = "no-store";
            if (options.Value.ReturnUrl is { Length: > 0 } ret)
                return Results.Redirect($"{ret}{(ret.Contains('?', StringComparison.Ordinal) ? '&' : '?')}paymentId={result.PaymentId}&status={result.Status}");
            return Results.Ok(result);
        }
        var g = api.MapGroup("/finance/gateway").WithTags("Payment gateway callbacks").AllowAnonymous();
        g.MapGet("/{provider}/callback/{paymentId:guid}", Handle);
        g.MapPost("/{provider}/callback/{paymentId:guid}", Handle).DisableAntiforgery();
    }

    private static string CallbackBase(FinancePaymentOptions o, HttpContext http) =>
        o.CallbackBaseUrl is { Length: > 0 } b ? b : $"{http.Request.Scheme}://{http.Request.Host}";

    private static bool IsCsv(string? format) => string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);

    private static IResult CsvFile(HttpContext http, string name, IReadOnlyList<string> header, IEnumerable<IReadOnlyList<object?>> rows)
    {
        http.Response.Headers.CacheControl = "no-store";
        return Results.File(Csv.Write(header, rows), "text/csv; charset=utf-8", name);
    }

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

    private static IResult Pdf(HttpContext http, byte[] bytes, string fileName)
    {
        http.Response.Headers.CacheControl = "no-store";
        return Results.File(bytes, "application/pdf", fileName);
    }
}
