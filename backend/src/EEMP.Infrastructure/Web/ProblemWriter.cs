using System.Diagnostics;
using System.Text.Json;
using EEMP.Infrastructure.Audit;
using EEMP.Infrastructure.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EEMP.Infrastructure.Web;

/// <summary>Writes RFC 7807 problem+json with the EEMP extensions: code, params, errors, traceId.</summary>
public static class ProblemWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task WriteAsync(
        HttpContext http,
        int status,
        string code,
        IReadOnlyDictionary<string, object?>? parameters = null,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        if (http.Response.HasStarted) return;
        var localizer = http.RequestServices.GetRequiredService<IServerTextLocalizer>();
        var traceId = Activity.Current?.TraceId.ToString() ?? http.TraceIdentifier;
        var body = new Dictionary<string, object?>
        {
            ["type"] = $"urn:eemp:error:{code}",
            ["title"] = localizer.Get(code),
            ["status"] = status,
            ["instance"] = http.Request.Path.Value,
            ["code"] = code,
            ["params"] = parameters ?? new Dictionary<string, object?>(),
            ["errors"] = errors ?? new Dictionary<string, string[]>(),
            ["traceId"] = traceId,
        };
        if (http.Items[CorrelationIdConstants.ItemKey] is string cid) body["correlationId"] = cid;

        http.Response.StatusCode = status;
        http.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(http.Response.Body, body, Json, http.RequestAborted);
    }
}
