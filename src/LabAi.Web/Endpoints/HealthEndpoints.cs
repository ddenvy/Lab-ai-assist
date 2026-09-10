using LabAi.Infrastructure.Ai;
using LabAi.Web.Infrastructure;

namespace LabAi.Web.Endpoints;

/// <summary>
/// Liveness and configuration probe. Reports only non-sensitive facts — in particular whether an
/// API key is present, never the key itself (AGENT.md 3.2).
/// </summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", static (GeminiOptions gemini, HttpContext context) =>
        {
            // "degraded" rather than a failure status: the process still serves every non-AI route,
            // and the test suite must be able to run the host with no key at all.
            var response = new HealthResponse(
                Status: gemini.IsConfigured ? "healthy" : "degraded",
                GeminiKeyPresent: gemini.IsConfigured,
                ChatModel: gemini.ChatModelId,
                EmbeddingModel: gemini.EmbeddingModelId,
                CorrelationId: context.Items[CorrelationIdMiddleware.ContextItemKey] as string);

            return Results.Ok(response);
        });

        return endpoints;
    }
}

/// <summary>Body of <c>GET /health</c>.</summary>
public sealed record HealthResponse(
    string Status,
    bool GeminiKeyPresent,
    string ChatModel,
    string EmbeddingModel,
    string? CorrelationId);
