using System.Diagnostics;

namespace LeadManagement.Api.Services;

public sealed class RequestDiagnosticsMiddleware(
    RequestDelegate next, ILogger<RequestDiagnosticsMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var elapsed = Stopwatch.StartNew();
        context.Response.Headers["X-Request-Id"] = context.TraceIdentifier;

        try
        {
            await next(context);
            if (context.Response.StatusCode >= 500)
                logger.LogError(
                    "CRM request failed {Method} {Path} {StatusCode} in {ElapsedMs}ms ({RequestId})",
                    context.Request.Method, context.Request.Path.Value,
                    context.Response.StatusCode, elapsed.ElapsedMilliseconds, context.TraceIdentifier);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "CRM exception {Method} {Path} in {ElapsedMs}ms ({RequestId})",
                context.Request.Method, context.Request.Path.Value,
                elapsed.ElapsedMilliseconds, context.TraceIdentifier);
            throw;
        }
    }
}
