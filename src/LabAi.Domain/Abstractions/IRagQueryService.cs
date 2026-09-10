using LabAi.Domain.ValueObjects;

namespace LabAi.Domain.Abstractions;

/// <summary>Port for the RAG query pipeline. Infrastructure implements this; Application orchestrates.</summary>
public interface IRagQueryService
{
    Task<RagAnswer> AskAsync(string question, long actorUserId, string correlationId, CancellationToken cancellationToken = default);
}
