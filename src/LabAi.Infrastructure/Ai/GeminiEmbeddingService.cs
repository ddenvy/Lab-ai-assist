using LabAi.Domain.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace LabAi.Infrastructure.Ai;

/// <summary>
/// Adapter over the connector's <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/>. The second of
/// the two files allowed to name a Semantic Kernel / Microsoft.Extensions.AI type (docs/adr/ADR-0002).
/// </summary>
/// <remarks>
/// Targets <c>AddGoogleAIEmbeddingGenerator</c>, not <c>AddGoogleAIEmbeddingGeneration</c>: the latter
/// carries <c>[Obsolete]</c> in 1.80.1-alpha and returns SK's <c>ITextEmbeddingGenerationService</c>
/// instead of the <c>Microsoft.Extensions.AI</c> generator the connector is converging on.
/// </remarks>
/// <param name="options">Resolved Gemini settings.</param>
/// <param name="resolveGenerator">Lazily resolved for the same reason as in <see cref="GeminiChatClient"/>.</param>
public sealed class GeminiEmbeddingService(
    GeminiOptions options,
    Func<IEmbeddingGenerator<string, Embedding<float>>> resolveGenerator,
    ILogger<GeminiEmbeddingService> logger) : IEmbeddingService
{
    public string ModelId => options.EmbeddingModelId;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (!options.IsConfigured)
            throw new GeminiNotConfiguredException();

        if (texts.Count == 0)
            return [];

        // Deliberately no EmbeddingGenerationOptions: the generator was registered with the
        // configured model id, and overriding it per call is exactly how a store ends up holding two
        // incomparable vector spaces. A model change must be a configuration change plus a re-ingest.
        var generated = await resolveGenerator()
            .GenerateAsync(texts, options: null, cancellationToken)
            .ConfigureAwait(false);

        var vectors = generated.Select(embedding => embedding.Vector.ToArray()).ToList();

        if (vectors.Count != texts.Count)
            throw new InvalidOperationException(
                $"Gemini returned {vectors.Count} embeddings for {texts.Count} inputs. " +
                "Vectors must arrive one per input and in input order, otherwise chunk embeddings " +
                "would be silently attributed to the wrong text.");

        // AGENT.md 3.2: counts and dimension only, never the embedded text.
        logger.LogInformation(
            "Gemini embeddings generated on {ModelId}: {TextCount} texts, dimension {Dimension}",
            ModelId,
            texts.Count,
            vectors[0].Length);

        return vectors;
    }
}
