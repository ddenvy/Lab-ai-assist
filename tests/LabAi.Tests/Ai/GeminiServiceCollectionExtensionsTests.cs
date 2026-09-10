using LabAi.Domain.Abstractions;
using LabAi.Infrastructure.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace LabAi.Tests.Ai;

/// <summary>
/// Proves the degraded-mode design of ADR-0002 (decision 5): the container builds and both ports
/// resolve with no API key at all, and the failure surfaces only when an AI operation is attempted.
/// Without this, the offline golden-set and E2E suites could not host the application.
/// </summary>
public sealed class GeminiServiceCollectionExtensionsTests
{
    [Fact]
    public void BuildsTheGraph_AndResolvesBothPorts_WithNoKey()
    {
        using var provider = BuildProvider(new GeminiOptions());

        provider.GetRequiredService<IGroundedChatClient>().Should().NotBeNull();
        provider.GetRequiredService<IEmbeddingService>().Should().NotBeNull();
    }

    [Fact]
    public async Task ChatFailsWithAnActionableError_WhenUsedWithoutAKey()
    {
        using var provider = BuildProvider(new GeminiOptions());
        var chat = provider.GetRequiredService<IGroundedChatClient>();

        Func<Task> act = () => chat.CompleteAsync("system", "user");

        var message = (await act.Should().ThrowAsync<GeminiNotConfiguredException>()).Which.Message;

        message.Should().Contain("GEMINI_API_KEY").And.Contain(".env.example");
    }

    [Fact]
    public async Task EmbeddingFailsWithAnActionableError_WhenUsedWithoutAKey()
    {
        using var provider = BuildProvider(new GeminiOptions());
        var embeddings = provider.GetRequiredService<IEmbeddingService>();

        Func<Task> act = () => embeddings.EmbedAsync(["some text"]);

        await act.Should().ThrowAsync<GeminiNotConfiguredException>();
    }

    [Fact]
    public void ExposesTheConfiguredEmbeddingModel_ThroughThePort()
    {
        using var provider = BuildProvider(new GeminiOptions
        {
            ApiKey = "not-a-real-key",
            EmbeddingModelId = "gemini-embedding-001",
        });

        provider.GetRequiredService<IEmbeddingService>().ModelId
            .Should().Be("gemini-embedding-001");
    }

    private static ServiceProvider BuildProvider(GeminiOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLabAiGemini(options);
        return services.BuildServiceProvider();
    }
}
