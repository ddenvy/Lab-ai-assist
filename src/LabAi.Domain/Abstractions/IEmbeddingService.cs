namespace LabAi.Domain.Abstractions;

/// <summary>
/// Port over the embedding model used for indexing documents and queries.
/// </summary>
/// <remarks>
/// Our own interface rather than Semantic Kernel's or <c>Microsoft.Extensions.AI</c>'s, for the same
/// reasons as <see cref="IGroundedChatClient"/>: the layers stay framework-free, and the golden-set
/// suite needs a deterministic stub implementing this exact contract so CI runs with no API key
/// (see docs/adr/ADR-0002).
/// </remarks>
public interface IEmbeddingService
{
    /// <summary>
    /// Model whose vectors this service produces. Persisted on every chunk: vectors from different
    /// models are not comparable, and the vector store refuses to search a store holding more than
    /// one (model, dimension) pair.
    /// </summary>
    string ModelId { get; }

    /// <summary>
    /// Embeds a batch of texts.
    /// </summary>
    /// <param name="texts">Input texts. Batching is the caller's decision, so the caller also owns
    /// retry and rate-limit behaviour.</param>
    /// <returns>
    /// One vector per input, in input order. Vectors are returned exactly as the model produced them:
    /// L2 normalization is applied by the ingest pipeline at write time, so the search hot path can
    /// rely on unit vectors and skip it entirely.
    /// </returns>
    Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
