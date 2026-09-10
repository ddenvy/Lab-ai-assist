namespace LabAi.Domain.Abstractions;

/// <summary>
/// Persistence port for documents and their chunks. The ingest pipeline owns the domain decisions
/// (idempotency by hash, which version supersedes which); this port owns the storage details,
/// including the single transaction that must cover inserting a new version and marking the
/// previous one superseded — a half-applied version change would leave two active documents.
/// </summary>
public interface IDocumentRepository
{
    /// <summary>Finds the active document with exactly these content bytes, or null.</summary>
    Task<Entities.SourceDocument?> FindActiveByContentHashAsync(string contentHash, CancellationToken cancellationToken = default);

    /// <summary>Finds the active document loaded from this path, or null.</summary>
    Task<Entities.SourceDocument?> FindActiveBySourcePathAsync(string sourcePath, CancellationToken cancellationToken = default);

    /// <summary>Current-version documents, ordered by title — the documents page and API listing.</summary>
    Task<IReadOnlyList<Entities.SourceDocument>> ListActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the document and its chunks and — in the same transaction — marks the previous
    /// active version superseded. <paramref name="buildChunks"/> is deferred on purpose: chunk rows
    /// reference the document id, which only exists once the store has generated it.
    /// </summary>
    /// <returns>The generated document id.</returns>
    Task<long> AddAsync(
        Entities.SourceDocument document,
        Func<long, IReadOnlyList<Entities.Chunk>> buildChunks,
        long? supersededDocumentId,
        CancellationToken cancellationToken = default);

    /// <summary>Retrieves chunks by their IDs for citation metadata resolution.</summary>
    Task<IReadOnlyDictionary<long, Entities.Chunk>> GetChunksByIdsAsync(IReadOnlyList<long> chunkIds, CancellationToken cancellationToken = default);
}
