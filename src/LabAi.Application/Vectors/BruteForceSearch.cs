using LabAi.Domain.ValueObjects;

namespace LabAi.Application.Vectors;

/// <summary>
/// Exhaustive scan over a <see cref="SearchIndex"/> — the O(n·d) baseline ADR-0001 accepts for
/// the demo's scale. The top-K selection is an insertion-sorted fixed buffer: no full sort of
/// the score set and no heap, only the destination span the caller provides (zero allocation).
/// </summary>
public static class BruteForceSearch
{
    /// <summary>
    /// Finds the <paramref name="k"/> highest-scoring neighbours with score ≥ <paramref name="minScore"/>,
    /// written to <paramref name="destination"/> in descending score order. Equal scores break by
    /// ascending chunk id, so the ordering is deterministic across runs — a retrieval result the
    /// audit journal cites must never depend on scan instability. Returns how many hits were written.
    /// </summary>
    public static int TopK(SearchIndex index, ReadOnlySpan<float> query, int k, float minScore, Span<SearchHit> destination)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (k < 0)
            throw new ArgumentOutOfRangeException(nameof(k), k, "k must not be negative.");
        if (k == 0)
            return 0;
        if (destination.Length < k)
            throw new ArgumentException(
                $"Destination span must hold k={k} hits, got {destination.Length}.", nameof(destination));
        if (query.Length != index.Dimension)
            throw new ArgumentException(
                $"Query dimension {query.Length} does not match index dimension {index.Dimension}.",
                nameof(query));

        var taken = 0;
        var vectors = index.Vectors;
        var dimension = index.Dimension;

        for (var row = 0; row < index.Count; row++)
        {
            var score = VectorMath.DotProduct(vectors.AsSpan(row * dimension, dimension), query);
            if (score < minScore)
                continue;

            var candidate = new SearchHit(index.ChunkIds[row], index.DocumentIds[row], score);
            if (taken == k && !Beats(candidate, destination[taken - 1]))
                continue;

            var position = Math.Min(taken, k - 1);
            while (position > 0 && Beats(candidate, destination[position - 1]))
            {
                destination[position] = destination[position - 1];
                position--;
            }

            destination[position] = candidate;
            if (taken < k)
                taken++;
        }

        return taken;
    }

    private static bool Beats(SearchHit candidate, SearchHit incumbent) =>
        candidate.Score > incumbent.Score
        || (candidate.Score == incumbent.Score && candidate.ChunkId < incumbent.ChunkId);
}
