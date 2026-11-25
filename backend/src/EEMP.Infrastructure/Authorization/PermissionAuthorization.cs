using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace EEMP.Infrastructure.Authorization;

public static class Policies
{
    public const string PermissionPrefix = "perm:";
    public const string PlatformAdmin = "platform";
    public const string TenantUser = "tenant";

    public static string Permission(string code) => PermissionPrefix + code;
}

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public sealed class PlatformAdminRequirement : IAuthorizationRequirement;

public sealed class TenantUserRequirement : IAuthorizationRequirement;

/// <summary>Builds policies dynamically: "perm:{code}" requires the permission in some scope of the caller's tenant.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(Policies.PermissionPrefix, StringComparison.Ordinal))
        {
            var code = policyName[Policies.PermissionPrefix.Length..];
            if (!Permissions.Exists(code)) throw new InvalidOperationException($"Unknown permission '{code}'.");
            return new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
                .AddRequirements(new TenantUserRequirement(), new PermissionRequirement(code)).Build();
        }

        return policyName switch
        {
            Policies.PlatformAdmin => new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new PlatformAdminRequirement()).Build(),
            Policies.TenantUser => new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new TenantUserRequirement()).Build(),
            _ => await base.GetPolicyAsync(policyName),
        };
    }
}

public sealed class EempAuthorizationHandler(ITenantContext tenant, IAccessControl access) : IAuthorizationHandler
{
    public async Task HandleAsync(AuthorizationHandlerContext context)
    {
        foreach (var requirement in context.PendingRequirements.ToList())
        {
            switch (requirement)
            {
                case PlatformAdminRequirement:
                    if (tenant.IsAuthenticated && tenant.IsPlatformAdmin) context.Succeed(requirement);
                    else context.Fail(new AuthorizationFailureReason(this, ErrorCodes.Forbidden));
                    break;
                case TenantUserRequirement:
                    if (tenant.IsAuthenticated && tenant.TenantId is not null) context.Succeed(requirement);
                    else context.Fail(new AuthorizationFailureReason(this, ErrorCodes.TenantForbidden));
                    break;
                case PermissionRequirement p:
                    if (tenant.TenantId is not null && await access.HasAnyAsync(p.Permission)) context.Succeed(requirement);
                    else context.Fail(new AuthorizationFailureReason(this, ErrorCodes.Forbidden));
                    break;
            }
        }
    }
}

/// <summary>Writes problem+json for 401/403 produced by the authorization middleware, carrying the failure code.</summary>
public sealed class ProblemAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            await Web.ProblemWriter.WriteAsync(context, 401, context.Items["eemp.auth_failure"] as string ?? ErrorCodes.Unauthorized);
            return;
        }
        if (authorizeResult.Forbidden)
        {
            var code = authorizeResult.AuthorizationFailure?.FailureReasons.Select(r => r.Message).FirstOrDefault() ?? ErrorCodes.Forbidden;
            await Web.ProblemWriter.WriteAsync(context, 403, code);
            return;
        }
        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}

public static class EndpointAuthorizationExtensions
{
    /// <summary>Declares the permission an endpoint requires (coarse gate; resource/branch checks happen in the application service).</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission) where TBuilder : IEndpointConventionBuilder
        => builder.RequireAuthorization(Policies.Permission(permission));
}
