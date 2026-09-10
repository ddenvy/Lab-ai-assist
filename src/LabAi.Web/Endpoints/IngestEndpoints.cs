using System.Globalization;
using System.Security.Claims;
using LabAi.Domain.Abstractions;
using LabAi.Domain.Enums;
using LabAi.Domain.ValueObjects;
using LabAi.Infrastructure.Ai;
using Microsoft.AspNetCore.Mvc;

namespace LabAi.Web.Endpoints;

/// <summary>
/// Ingest of source documents and listing of the active corpus. The endpoint contains no business
/// logic: kind detection from the file name is HTTP-surface translation, everything else is
/// delegated to <see cref="IDocumentIngestService"/>. Uploading is limited to Analyst and
/// Administrator because ingest permanently changes the retrievable corpus and the journal must
/// name a qualified actor.
/// </summary>
public static class IngestEndpoints
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    public static IEndpointRouteBuilder MapIngestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/ingest", static async (
                IFormFile? file,
                [FromForm] string? title,
                [FromForm] int? version,
                IDocumentIngestService ingest,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                // AGENT.md 3.3: untrusted multipart input is bounded and validated before it
                // reaches the pipeline.
                var errors = new Dictionary<string, string[]>();
                if (file is null || file.Length == 0)
                    errors["file"] = ["File is required."];
                else if (file.Length > MaxUploadBytes)
                    errors["file"] = [$"File exceeds the {MaxUploadBytes / (1024 * 1024)} MB limit."];

                if (string.IsNullOrWhiteSpace(title))
                    errors["title"] = ["Title is required."];

                if (version is null or < 1)
                    errors["version"] = ["Version must be 1 or greater."];

                DocumentKind? kind = null;
                if (file is not null && (errors.Count == 0 || !errors.ContainsKey("file")))
                {
                    kind = KindFromFileName(file.FileName);
                    if (kind is null)
                        errors["file"] = ["Unsupported document type. Allowed: .md, .markdown, .csv, .json, .pdf."];
                }

                if (errors.Count > 0)
                    return Results.ValidationProblem(errors);

                var userIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!long.TryParse(userIdClaim, CultureInfo.InvariantCulture, out var ingestedByUserId))
                    return Results.Unauthorized();

                await using var source = file!.OpenReadStream();
                using var buffer = new MemoryStream();
                await source.CopyToAsync(buffer, cancellationToken);

                try
                {
                    var result = await ingest.IngestAsync(
                        new IngestRequest(
                            file.FileName,
                            title!,
                            version!.Value,
                            kind!.Value,
                            buffer.ToArray(),
                            ingestedByUserId),
                        cancellationToken);

                    return Results.Ok(new IngestResponse(
                        result.DocumentId,
                        result.ChunksCreated,
                        result.Outcome.ToString(),
                        result.SupersededDocumentId));
                }
                catch (GeminiNotConfiguredException exception)
                {
                    // 503, not 500: nothing is broken, a required key is simply absent — same
                    // contract as the chat endpoint. Ingest retries safely (idempotent by hash).
                    return Results.Problem(
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        detail: exception.Message);
                }
            })
            .RequireAuthorization(static policy => policy.RequireRole(
                nameof(UserRole.Analyst), nameof(UserRole.Administrator)))
            // External API clients (curl, Mini-CDS) cannot present antiforgery tokens; cross-site
            // POSTs are handled by the auth cookie being SameSite=Strict.
            .DisableAntiforgery();

        endpoints.MapGet("/api/documents", static async (
                IDocumentRepository repository,
                CancellationToken cancellationToken) =>
            {
                var documents = await repository.ListActiveAsync(cancellationToken);
                return Results.Ok(documents.Select(static d => new DocumentResponse(
                    d.Id,
                    d.Title,
                    d.SourcePath,
                    d.Version,
                    d.Kind.ToString(),
                    d.IngestedAtUtc)));
            })
            .RequireAuthorization();

        return endpoints;
    }

    private static DocumentKind? KindFromFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".md" or ".markdown" => DocumentKind.Markdown,
            ".csv" => DocumentKind.Csv,
            ".json" => DocumentKind.Json,
            ".pdf" => DocumentKind.Pdf,
            _ => null,
        };
    }
}

/// <summary>Response of <c>POST /api/ingest</c>.</summary>
public sealed record IngestResponse(long DocumentId, int ChunksCreated, string Outcome, long? SupersededDocumentId);

/// <summary>Element of <c>GET /api/documents</c>.</summary>
public sealed record DocumentResponse(long Id, string Title, string SourcePath, int Version, string Kind, DateTime IngestedAtUtc);
