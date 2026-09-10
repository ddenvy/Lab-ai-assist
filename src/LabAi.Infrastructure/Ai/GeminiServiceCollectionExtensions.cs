using LabAi.Domain.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Google;

namespace LabAi.Infrastructure.Ai;

/// <summary>
/// Registers the Gemini adapters behind our own ports. Nothing above Infrastructure ever sees a
/// Semantic Kernel type.
/// </summary>
public static class GeminiServiceCollectionExtensions
{
    public static IServiceCollection AddLabAiGemini(this IServiceCollection services, GeminiOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        // Both connector extensions register a lazy factory, so an empty key is accepted here and only
        // throws on resolution (verified by reflection probe against 1.80.1-alpha). Registering them
        // unconditionally keeps the graph identical with and without a key, which is what lets the
        // adapters be constructed either way and fail with GeminiNotConfiguredException on first use.
        //
        // GoogleAIVersion is passed explicitly even though V1_Beta is already the default: an alpha
        // package quietly changing its own default would change the meaning of every stored embedding.
        services.AddGoogleAIGeminiChatCompletion(options.ChatModelId, options.ApiKey, GoogleAIVersion.V1_Beta);
        services.AddGoogleAIEmbeddingGenerator(options.EmbeddingModelId, options.ApiKey, GoogleAIVersion.V1_Beta);

        services.AddSingleton<IGroundedChatClient>(provider => new GeminiChatClient(
            options,
            () => provider.GetRequiredService<IChatCompletionService>(),
            provider.GetRequiredService<ILogger<GeminiChatClient>>()));

        services.AddSingleton<IEmbeddingService>(provider => new GeminiEmbeddingService(
            options,
            () => provider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
            provider.GetRequiredService<ILogger<GeminiEmbeddingService>>()));

        return services;
    }
}
