using EEMP.SharedKernel.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EEMP.Infrastructure.Web;

/// <summary>Maps exceptions to problem+json. Internal details are logged (without request bodies) and never returned.</summary>
public sealed class AppExceptionHandler(ILogger<AppExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var http = httpContext;
        switch (exception)
        {
            case ValidationException v:
                await ProblemWriter.WriteAsync(http, 400, v.Code, v.Parameters, v.Errors);
                return true;
            case AppException app:
                if (app.StatusCode == 404 || app.StatusCode == 403)
                    logger.LogInformation("Access denied or missing resource {Code} for {Path}", app.Code, http.Request.Path.Value);
                await ProblemWriter.WriteAsync(http, app.StatusCode, app.Code, app.Parameters);
                return true;
            case DbUpdateConcurrencyException:
                await ProblemWriter.WriteAsync(http, 409, ErrorCodes.ConcurrencyConflict);
                return true;
            case BadHttpRequestException bad:
                logger.LogInformation("Malformed request: {Message}", bad.Message);
                await ProblemWriter.WriteAsync(http, 400, ErrorCodes.ValidationFailed,
                    errors: new Dictionary<string, string[]> { ["body"] = ["validation.invalid_format"] });
                return true;
            default:
                logger.LogError(exception, "Unhandled exception for {Method} {Path}", http.Request.Method, http.Request.Path.Value);
                await ProblemWriter.WriteAsync(http, 500, ErrorCodes.InternalError);
                return true;
        }
    }
}
