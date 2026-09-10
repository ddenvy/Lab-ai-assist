namespace LabAi.Application.Vectors;

/// <summary>
/// Immutable snapshot of the vector store as parallel arrays (SoA, AGENT.md 2.4): one flat
/// float buffer holds every vector back to back, so the scan is a single loop over contiguous
/// memory with no per-hit indirection. Published by <c>EfVectorStore</c> through
/// <c>Volatile.Write</c> — readers never block, and the sole writer is the ingest pipeline.
/// Vectors of superseded documents are simply absent: staleness is decided at snapshot build
/// time, not in the hot loop.
/// </summary>
public sealed class SearchIndex
{
    public static readonly SearchIndex Empty = new([], [], [], 0);

    public SearchIndex(long[] chunkIds, long[] documentIds, float[] vectors, int dimension)
    {
        if (chunkIds.Length != documentIds.Length)
            throw new ArgumentException(
                $"Chunk and document id arrays must have the same length, got {chunkIds.Length} and {documentIds.Length}.",
                nameof(documentIds));
        if (vectors.Length != (long)chunkIds.Length * dimension)
            throw new ArgumentException(
                $"Vector buffer must hold chunkIds.Length * dimension floats, got {vectors.Length} for {chunkIds.Length} chunks of dimension {dimension}.",
                nameof(vectors));

        ChunkIds = chunkIds;
        DocumentIds = documentIds;
        Vectors = vectors;
        Dimension = dimension;
        Count = chunkIds.Length;
    }

    public long[] ChunkIds { get; }

    public long[] DocumentIds { get; }

    /// <summary>Flat buffer of <c>Count * Dimension</c> floats; row <c>i</c> starts at <c>i * Dimension</c>.</summary>
    public float[] Vectors { get; }

    public int Dimension { get; }

    public int Count { get; }
}
