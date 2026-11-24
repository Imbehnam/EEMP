using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using EEMP.Infrastructure.Audit;
using EEMP.Infrastructure.Authorization;
using EEMP.Infrastructure.Localization;
using EEMP.Infrastructure.Persistence;
using EEMP.Infrastructure.Tenancy;
using EEMP.Infrastructure.Web;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EEMP.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddEempInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddOptions<RateLimitingOptions>().Bind(configuration.GetSection(RateLimitingOptions.SectionName));
        ConfigureForwardedHeaders(services, configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<PersistenceInterceptor>();
        services.AddModuleDbContext<AuditDbContext>(AuditDbContext.SchemaName, intercept: false);
        services.AddSingleton<IServerTextLocalizer, ServerTextLocalizer>();

        services.AddExceptionHandler<AppExceptionHandler>();
        services.AddProblemDetails();

        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, EempAuthorizationHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();
        services.AddAuthorization();

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(RateLimitingOptions.AuthPolicy, http =>
            {
                var opts = http.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                var key = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = opts.AuthPermitLimit,
                    Window = TimeSpan.FromSeconds(opts.AuthWindowSeconds),
                    QueueLimit = 0,
                });
            });
            o.OnRejected = async (ctx, _) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    ctx.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                await ProblemWriter.WriteAsync(ctx.HttpContext, 429, ErrorCodes.RateLimited);
            };
        });

        services.Configure<RequestLocalizationOptions>(o =>
        {
            var cultures = new[] { "fa-IR", "fa", "en", "tr", "ar" }.Select(c => new CultureInfo(c)).ToList();
            o.DefaultRequestCulture = new RequestCulture("fa-IR");
            o.SupportedCultures = cultures;
            o.SupportedUICultures = cultures;
            o.FallBackToParentCultures = true;
            o.FallBackToParentUICultures = true;
            o.RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider()];
        });

        return services;
    }

    /// <summary>
    /// The web apps call the API through their server-side BFF, so without this every user would share the BFF's
    /// address and one per-IP auth rate-limit bucket. X-Forwarded-For is honoured only from trusted proxies:
    /// loopback by default (BFF on the same host) plus <c>ReverseProxy:KnownProxies</c> / <c>ReverseProxy:KnownNetworks</c>
    /// (e.g. the web containers' network in Docker). Only the last hop is used (ForwardLimit = 1).
    /// </summary>
    private static void ConfigureForwardedHeaders(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("ReverseProxy");
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            o.ForwardLimit = 1;
            foreach (var ip in section.GetSection("KnownProxies").Get<string[]>() ?? [])
                if (IPAddress.TryParse(ip, out var address)) o.KnownProxies.Add(address);
            foreach (var cidr in section.GetSection("KnownNetworks").Get<string[]>() ?? [])
                if (System.Net.IPNetwork.TryParse(cidr, out var network)) o.KnownIPNetworks.Add(network);
        });
    }

    /// <summary>Pipeline pieces that must run before authentication.</summary>
    public static IApplicationBuilder UseEempRequestPipeline(this IApplicationBuilder app, Action<IApplicationBuilder>? requestLogging = null)
    {
        app.UseForwardedHeaders();
        app.UseMiddleware<CorrelationIdMiddleware>();
        // Request logging sits outside the exception handler so it records the final (mapped) status code.
        requestLogging?.Invoke(app);
        app.UseRequestLocalization();
        app.UseExceptionHandler();
        app.UseStatusCodePages(async ctx =>
        {
            var http = ctx.HttpContext;
            var code = http.Response.StatusCode switch
            {
                404 => ErrorCodes.NotFound,
                401 => ErrorCodes.Unauthorized,
                403 => ErrorCodes.Forbidden,
                405 => ErrorCodes.NotFound,
                415 => ErrorCodes.ValidationFailed,
                _ => ErrorCodes.InternalError,
            };
            await ProblemWriter.WriteAsync(http, http.Response.StatusCode, code);
        });
        return app;
    }
}
