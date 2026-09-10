using LabAi.Domain.Abstractions;
using LabAi.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace LabAi.Web.Endpoints;

/// <summary>
/// Grounded Q&A endpoint. Requires authentication so that ActorUserId is real for the audit journal.
/// Rate-limited to 10 requests per minute per user (configured in Program.cs).
/// </summary>
public static class AskEndpoints
{
    private const int MaxQuestionChars = 4000;

    public static IEndpointRouteBuilder MapAskEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/ask", [Authorize] static async (
            AskRequest request,
            IRagQueryService rag,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Question))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(AskRequest.Question)] = ["Question is required."],
                });
            }

            if (request.Question.Length > MaxQuestionChars)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(AskRequest.Question)] = [$"Question must be {MaxQuestionChars} characters or fewer."],
                });
            }

            var userId = context.User.GetUserId();
            if (userId == null)
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, detail: "User not authenticated.");
            }

            var correlationId = context.Items[CorrelationIdMiddleware.ContextItemKey] as string ?? Guid.NewGuid().ToString("N");

            try
            {
                var answer = await rag.AskAsync(request.Question, userId.Value, correlationId, cancellationToken);

                return Results.Ok(new AskResponse(
                    Answer: answer.Answer,
                    AnsweredFromContext: answer.AnsweredFromContext,
                    RefusalStage: answer.RefusalStage.ToString(),
                    Citations: answer.Citations.Select(c => new SourceCitation(c.ChunkId, c.DocumentId, c.Score)).ToList(),
                    Rationale: answer.Rationale,
                    AuditEntryId: answer.AuditEntryId,
                    CorrelationId: answer.CorrelationId));
            }
            catch (OperationCanceledException)
            {
                return Results.Problem(statusCode: StatusCodes.Status499ClientClosedRequest, detail: "Request was cancelled.");
            }
            catch (Exception ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, detail: ex.Message);
            }
        });

        return endpoints;
    }
}

public sealed record AskRequest(string? Question);

public sealed record AskResponse(
    string Answer,
    bool AnsweredFromContext,
    string RefusalStage,
    IReadOnlyList<SourceCitation> Citations,
    string? Rationale,
    long AuditEntryId,
    string CorrelationId);

public sealed record SourceCitation(long ChunkId, long DocumentId, double Score);
