using Serilog.Context;

namespace LabAi.Web.Infrastructure;

/// <summary>
/// Assigns every request a correlation id and pushes it into the Serilog log context, so all log
/// lines produced while handling one question can be tied back to the AI audit entry that recorded
/// it (AGENT.md 7.1). The id is echoed on the response and surfaced in the UI next to the answer.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    /// <summary>Header used both to accept an upstream id and to return the effective one.</summary>
    public const string HeaderName = "X-Correlation-Id";

    /// <summary><see cref="HttpContext.Items"/> key holding the effective correlation id.</summary>
    public const string ContextItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        context.Items[ContextItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        // An upstream id wins: a caller that already correlates (e.g. Mini-CDS in the A+C
        // integration) must not have its chain broken by a freshly generated one.
        var incoming = context.Request.Headers[HeaderName].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(incoming))
            return incoming;

        var activityId = System.Diagnostics.Activity.Current?.Id;
        return string.IsNullOrWhiteSpace(activityId)
            ? Guid.NewGuid().ToString("N")
            : activityId;
    }
}
