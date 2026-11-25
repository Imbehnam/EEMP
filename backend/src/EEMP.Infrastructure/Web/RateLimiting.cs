namespace EEMP.Infrastructure.Web;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";
    public const string AuthPolicy = "auth";
    /// <summary>Auth endpoint requests allowed per client IP per window.</summary>
    public int AuthPermitLimit { get; set; } = 10;
    public int AuthWindowSeconds { get; set; } = 60;
}
