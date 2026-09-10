using LabAi.Application.Chunking;
using LabAi.Domain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LabAi.Infrastructure.Chunking;

public static class ChunkingServiceCollectionExtensions
{
    /// <summary>
    /// Registers one <see cref="IChunkingStrategy"/> set covering every document kind. Chunkers
    /// live in the Application layer (pure BCL) but are composed here, where the configuration
    /// values are read; the strategy contract keeps Application free of DI types.
    /// </summary>
    public static IServiceCollection AddLabAiChunking(this IServiceCollection services, int maxChunkChars, int overlapChars)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IChunkingStrategy>(new MarkdownChunker(maxChunkChars, overlapChars));
        services.AddSingleton<IChunkingStrategy>(new CsvRowGroupChunker(maxChunkChars));
        services.AddSingleton<IChunkingStrategy>(new GenericTextChunker(maxChunkChars, overlapChars));
        return services;
    }
}
