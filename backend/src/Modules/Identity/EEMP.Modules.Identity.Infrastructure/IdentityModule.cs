using EEMP.Infrastructure.Authorization;
using EEMP.Infrastructure.Persistence;
using EEMP.Infrastructure.Web;
using EEMP.Modules.Identity.Application;
using EEMP.Modules.Identity.Contracts;
using EEMP.Modules.Identity.Domain;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EEMP.Modules.Identity.Infrastructure;

public static class IdentityModule
{
    public const string AuthFailureItem = "eemp.auth_failure";

    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IdentityModuleDbContext>(IdentityModuleDbContext.SchemaName);
        services.AddScoped<IIdentityDbContext>(sp => sp.GetRequiredService<IdentityModuleDbContext>());

        services.AddIdentityCore<AppUser>(o =>
            {
                o.Password.RequiredLength = 8;
                o.Password.RequireDigit = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireUppercase = true;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;
                o.User.RequireUniqueEmail = false;
            })
            .AddEntityFrameworkStores<IdentityModuleDbContext>();

        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => o.SigningKey.Length >= 32, "Jwt:SigningKey must be at least 32 characters.")
            .Validate(o => o.AccessTokenMinutes is > 0 and <= 60, "Jwt:AccessTokenMinutes must be 1..60.")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((o, jwt) =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = jwt.Value.Key(),
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = EempClaims.Name,
                };
                o.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ValidateSessionAsync,
                    OnAuthenticationFailed = ctx =>
                    {
                        ctx.HttpContext.Items[AuthFailureItem] = ErrorCodes.Unauthorized;
                        return Task.CompletedTask;
                    },
                    OnChallenge = async ctx =>
                    {
                        ctx.HandleResponse();
                        await ProblemWriter.WriteAsync(ctx.HttpContext, 401, ctx.HttpContext.Items[AuthFailureItem] as string ?? ErrorCodes.Unauthorized);
                    },
                };
            });

        services.AddSingleton<IAccessTokenIssuer, JwtTokenIssuer>();
        services.AddScoped<IAuthAuditor, AuthAuditor>();
        services.AddScoped<AuthService>();
        services.AddScoped<RoleService>();
        services.AddScoped<UserService>();
        services.AddScoped<IAccessControl, AccessControl>();
        services.AddScoped<ITenantAccessProvisioner, TenantAccessProvisioner>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IAssignmentUsage, AssignmentUsage>();
        return services;
    }

    /// <summary>Every request with a valid signature is re-validated against the database (user, tenant, session) before the tenant context is set.</summary>
    private static async Task ValidateSessionAsync(TokenValidatedContext ctx)
    {
        var p = ctx.Principal!;
        if (!Guid.TryParse(p.FindFirst(EempClaims.Subject)?.Value, out var userId) ||
            !Guid.TryParse(p.FindFirst(EempClaims.SessionId)?.Value, out var sessionId))
        {
            ctx.HttpContext.Items[AuthFailureItem] = ErrorCodes.Unauthorized;
            ctx.Fail(ErrorCodes.Unauthorized);
            return;
        }
        var tenantClaim = p.FindFirst(EempClaims.TenantId)?.Value;
        Guid? tenantId = null;
        if (tenantClaim is not null)
        {
            if (!Guid.TryParse(tenantClaim, out var t)) { ctx.Fail(ErrorCodes.Unauthorized); return; }
            tenantId = t;
        }

        var services = ctx.HttpContext.RequestServices;
        var result = await services.GetRequiredService<AuthService>().ValidateSessionAsync(userId, tenantId, sessionId, ctx.HttpContext.RequestAborted);
        if (result.Error is not null)
        {
            ctx.HttpContext.Items[AuthFailureItem] = result.Error;
            ctx.Fail(result.Error);
            return;
        }
        services.GetRequiredService<ITenantContext>().Initialize(tenantId, userId, result.IsPlatformAdmin);
    }

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth");
        auth.MapPost("/login", async (LoginRequest req, AuthService s, CancellationToken ct) => Results.Ok(await s.LoginAsync(req, ct)))
            .AllowAnonymous().RequireRateLimiting(RateLimitingOptions.AuthPolicy);
        auth.MapPost("/refresh", async (RefreshRequest req, AuthService s, CancellationToken ct) => Results.Ok(await s.RefreshAsync(req, ct)))
            .AllowAnonymous().RequireRateLimiting(RateLimitingOptions.AuthPolicy);
        auth.MapPost("/logout", async (LogoutRequest req, AuthService s, CancellationToken ct) =>
            {
                await s.LogoutAsync(req, ct);
                return Results.NoContent();
            })
            .AllowAnonymous().RequireRateLimiting(RateLimitingOptions.AuthPolicy);
        auth.MapGet("/me", async (AuthService s, CancellationToken ct) => Results.Ok(await s.GetMeAsync(ct))).RequireAuthorization();
        auth.MapPut("/me/preferences", async (PreferencesRequest req, AuthService s, CancellationToken ct) =>
            Results.Ok(await s.UpdatePreferencesAsync(req, ct))).RequireAuthorization();

        api.MapGet("/permissions", () => Results.Ok(RoleService.Catalog()))
            .WithTags("Roles").RequirePermission(Permissions.RolesRead);

        var roles = api.MapGroup("/roles").WithTags("Roles");
        roles.MapGet("/", async (string? search, RoleService s, CancellationToken ct) => Results.Ok(await s.ListAsync(search, ct)))
            .RequirePermission(Permissions.RolesRead);
        roles.MapPost("/", async (CreateRoleRequest req, RoleService s, CancellationToken ct) =>
            {
                var r = await s.CreateAsync(req, ct);
                return Results.Created($"/api/v1/roles/{r.Id}", r);
            }).RequirePermission(Permissions.RolesManage);
        roles.MapGet("/{id:guid}", async (Guid id, RoleService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct)))
            .RequirePermission(Permissions.RolesRead);
        roles.MapPut("/{id:guid}", async (Guid id, UpdateRoleRequest req, RoleService s, CancellationToken ct) => Results.Ok(await s.UpdateAsync(id, req, ct)))
            .RequirePermission(Permissions.RolesManage);
        roles.MapDelete("/{id:guid}", async (Guid id, RoleService s, CancellationToken ct) =>
            {
                await s.DeleteAsync(id, ct);
                return Results.NoContent();
            }).RequirePermission(Permissions.RolesManage);

        var users = api.MapGroup("/users").WithTags("Users");
        users.MapGet("/", async ([AsParameters] PagingQuery q, UserService s, CancellationToken ct) => Results.Ok(await s.ListAsync(q.ToPageRequest(), ct)))
            .RequirePermission(Permissions.UsersRead);
        users.MapPost("/", async (CreateUserRequest req, UserService s, CancellationToken ct) =>
            {
                var u = await s.CreateAsync(req, ct);
                return Results.Created($"/api/v1/users/{u.Id}", u);
            }).RequirePermission(Permissions.UsersManage);
        users.MapGet("/{id:guid}", async (Guid id, UserService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct)))
            .RequirePermission(Permissions.UsersRead);
        users.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest req, UserService s, CancellationToken ct) => Results.Ok(await s.UpdateAsync(id, req, ct)))
            .RequirePermission(Permissions.UsersManage);
        users.MapPost("/{id:guid}/deactivate", async (Guid id, UserService s, CancellationToken ct) => Results.Ok(await s.SetActiveAsync(id, false, ct)))
            .RequirePermission(Permissions.UsersManage);
        users.MapPost("/{id:guid}/activate", async (Guid id, UserService s, CancellationToken ct) => Results.Ok(await s.SetActiveAsync(id, true, ct)))
            .RequirePermission(Permissions.UsersManage);
        users.MapPut("/{id:guid}/assignments", async (Guid id, List<AssignmentRequest> req, UserService s, CancellationToken ct) =>
                Results.Ok(await s.SetAssignmentsAsync(id, req, ct)))
            .RequirePermission(Permissions.UsersManage);
        return api;
    }
}
