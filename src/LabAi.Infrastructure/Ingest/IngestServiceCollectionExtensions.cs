using LabAi.Application.Ingest;
using LabAi.Domain.Abstractions;
using LabAi.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace LabAi.Infrastructure.Ingest;

public static class IngestServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ingest pipeline. Parsers, chunking strategies, embeddings and persistence are
    /// resolved from their own registrations; only the embedding batch size is supplied here, read
    /// from configuration by the composition root.
    /// </summary>
    public static IServiceCollection AddLabAiIngest(this IServiceCollection services, int embeddingBatchSize)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentOutOfRangeException.ThrowIfLessThan(embeddingBatchSize, 1);

        services.AddScoped<IDocumentIngestService>(sp => new IngestPipeline(
            sp.GetServices<IDocumentParser>(),
            sp.GetServices<IChunkingStrategy>(),
            sp.GetRequiredService<IEmbeddingService>(),
            sp.GetRequiredService<IDocumentRepository>(),
            embeddingBatchSize));

        return services;
    }
}
