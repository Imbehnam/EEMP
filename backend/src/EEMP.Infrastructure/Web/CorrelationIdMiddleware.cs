using System.Text.RegularExpressions;
using EEMP.Infrastructure.Audit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace EEMP.Infrastructure.Web;

/// <summary>Accepts a well-formed X-Correlation-Id or generates one, echoes it and adds it to the logging scope.</summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext http)
    {
        var incoming = http.Request.Headers[CorrelationIdConstants.HeaderName].ToString();
        var id = !string.IsNullOrEmpty(incoming) && Safe().IsMatch(incoming) ? incoming : Guid.NewGuid().ToString("N");
        http.Items[CorrelationIdConstants.ItemKey] = id;
        http.Response.OnStarting(() =>
        {
            http.Response.Headers[CorrelationIdConstants.HeaderName] = id;
            return Task.CompletedTask;
        });
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
        {
            await next(http);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9\\-_.]{1,64}$")]
    private static partial Regex Safe();
}
