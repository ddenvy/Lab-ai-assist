namespace LabAi.Domain.Abstractions;

/// <summary>
/// Persistence port for the vector store. The only writer is the ingest pipeline, which calls
/// <see cref="RebuildAsync"/> after committing a new version; every reader grabs the current
/// snapshot via <see cref="Snapshot"/> (volatile read) and runs <c>BruteForceSearch.TopK</c> on it.
/// </summary>
public interface IVectorStore
{
    /// <summary>The latest published snapshot — never null; starts as <c>SearchIndex.Empty</c>.</summary>
    ValueObjects.SearchIndex Snapshot { get; }

    /// <summary>
    /// Rebuilds the snapshot from the database: dimension guard (mixed models → exception),
    /// Active-only chunks, L2-normalised float32-LE blobs decoded into one flat buffer, then
    /// published atomically via Volatile.Write. Throws when the store holds embeddings from more
    /// than one model/dimension pair — the operator must re-ingest to restore a consistent space.
    /// </summary>
    Task RebuildAsync(CancellationToken cancellationToken = default);
}
