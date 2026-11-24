using System.Security.Claims;
using System.Text;
using EEMP.Modules.Identity.Application;
using EEMP.Modules.Identity.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EEMP.Modules.Identity.Infrastructure;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "eemp-api";
    public string Audience { get; set; } = "eemp-clients";
    /// <summary>HMAC-SHA256 key, at least 32 bytes. Supplied via environment/secret store only.</summary>
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;

    public SymmetricSecurityKey Key() => new(Encoding.UTF8.GetBytes(SigningKey));
}

public static class EempClaims
{
    public const string Subject = "sub";
    public const string TenantId = "tenant_id";
    public const string SessionId = "sid";
    public const string Name = "name";
}

public sealed class JwtTokenIssuer(IOptions<JwtOptions> options, TimeProvider clock) : IAccessTokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new();

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(options.Value.RefreshTokenDays);

    public IssuedAccessToken Issue(AppUser user, Guid sessionId)
    {
        var o = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(o.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(EempClaims.Subject, user.Id.ToString()),
            new(EempClaims.SessionId, sessionId.ToString()),
            new(EempClaims.Name, user.UserName ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        if (user.TenantId is { } tid) claims.Add(new Claim(EempClaims.TenantId, tid.ToString()));

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = o.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(o.Key(), SecurityAlgorithms.HmacSha256),
        });
        return new IssuedAccessToken(token, expires);
    }
}
