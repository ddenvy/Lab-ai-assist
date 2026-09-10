using LabAi.Domain.Abstractions;
using LabAi.Infrastructure.Ai;
using LabAi.Web.Infrastructure;

namespace LabAi.Web.Endpoints;

/// <summary>
/// Temporary smoke endpoint that proves the Gemini wiring before the RAG pipeline exists. It has no
/// grounding, no citations and writes no audit row, so it is replaced by <c>POST /api/ask</c> in
/// Milestone 4 and must not survive into the demo.
/// </summary>
public static class ChatEndpoints
{
    private const int MaxMessageChars = 4000;

    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/chat", static async (
            ChatRequest request,
            IGroundedChatClient chat,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            // AGENT.md 3.3: untrusted input is bounded before it reaches a paid external API.
            if (string.IsNullOrWhiteSpace(request.Message))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(ChatRequest.Message)] = ["Message is required."],
                });

            if (request.Message.Length > MaxMessageChars)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(ChatRequest.Message)] = [$"Message must be {MaxMessageChars} characters or fewer."],
                });

            try
            {
                var result = await chat.CompleteAsync(
                    systemPrompt: "You are a terse assistant. Reply in the language of the question.",
                    userPrompt: request.Message,
                    cancellationToken);

                return Results.Ok(new ChatResponse(
                    result.Text,
                    result.ModelId,
                    context.Items[CorrelationIdMiddleware.ContextItemKey] as string));
            }
            catch (GeminiNotConfiguredException exception)
            {
                // 503, not 500: nothing is broken, a required key is simply absent, and retrying
                // without changing the configuration would not help.
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    detail: exception.Message);
            }
        });

        return endpoints;
    }
}

/// <summary>Body of <c>POST /api/chat</c>.</summary>
public sealed record ChatRequest(string? Message);

/// <summary>Response of <c>POST /api/chat</c>.</summary>
public sealed record ChatResponse(string Reply, string Model, string? CorrelationId);
