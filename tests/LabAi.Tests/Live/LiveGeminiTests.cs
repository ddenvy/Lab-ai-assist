using LabAi.Domain.Abstractions;
using LabAi.Infrastructure.Ai;
using LabAi.Web.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LabAi.Tests.Live;

/// <summary>
/// Opt-in suite that talks to the real Gemini service. Every test skips itself when no
/// <c>GEMINI_API_KEY</c> is present, so a clean clone passes <c>dotnet test</c> offline and CI never
/// needs credentials. This suite is the only thing that detects alpha-connector breakage against the
/// live API — the offline tests prove our contract, not Google's.
/// </summary>
/// <remarks>
/// <c>[SkippableFact]</c> instead of <c>[Fact]</c>: xunit 2.9.3 has no <c>Assert.Skip</c>, and a
/// plain early return would report a skipped-by-environment test as passed.
/// </remarks>
[Trait("Category", "Live")]
public sealed class LiveGeminiTests
{
    [SkippableFact]
    public async Task Chat_ReturnsANonEmptyReply()
    {
        using var provider = ConfiguredProvider();
        var chat = provider.GetRequiredService<IGroundedChatClient>();

        var result = await chat.CompleteAsync(
            systemPrompt: "Reply with exactly one word.",
            userPrompt: "Say OK.");

        result.Text.Should().NotBeNullOrWhiteSpace();
        result.ModelId.Should().NotBeNullOrWhiteSpace();
    }

    [SkippableFact]
    public async Task ChatRespectsCancellation()
    {
        using var provider = ConfiguredProvider();
        var chat = provider.GetRequiredService<IGroundedChatClient>();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => chat.CompleteAsync("system", "user", cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [SkippableFact]
    public async Task Embeddings_ReturnOneVectorPerInput_AllOfOneDimension()
    {
        using var provider = ConfiguredProvider();
        var embeddings = provider.GetRequiredService<IEmbeddingService>();

        var vectors = await embeddings.EmbedAsync(
            ["HPLC system suitability", "analytical balance calibration", "pipette quality control"]);

        vectors.Should().HaveCount(3);
        vectors[0].Length.Should().BePositive();
        vectors.Select(vector => vector.Length).Distinct().Should().ContainSingle(
            "every vector from one model must share a dimension, or the store is not searchable");
    }

    [SkippableFact]
    public async Task Embeddings_AreStable_AcrossSeparateCalls()
    {
        using var provider = ConfiguredProvider();
        var embeddings = provider.GetRequiredService<IEmbeddingService>();

        var first = (await embeddings.EmbedAsync(["system suitability criteria"]))[0];
        var second = (await embeddings.EmbedAsync(["system suitability criteria"]))[0];

        Cosine(first, second).Should().BeGreaterThan(0.9999,
            "a retrieval index is meaningless if the same text embeds differently between calls");
    }

    [SkippableFact]
    public async Task Embeddings_SeparateUnrelatedTexts()
    {
        using var provider = ConfiguredProvider();
        var embeddings = provider.GetRequiredService<IEmbeddingService>();

        var vectors = await embeddings.EmbedAsync(
            ["HPLC system suitability acceptance criteria", "recipe for banana bread"]);

        Cosine(vectors[0], vectors[1]).Should().BeLessThan(0.99,
            "near-identical vectors for unrelated texts would mean the adapter is not forwarding the input");
    }

    /// <summary>
    /// Builds a provider through the same configuration path the host uses, or skips the calling test
    /// when no key is available. Loading <c>.env</c> here keeps "it works in the app" and "it works
    /// in the live suite" the same statement.
    /// </summary>
    private static ServiceProvider ConfiguredProvider()
    {
        DotNetEnv.Env.TraversePath().Load();
        var options = GeminiOptionsFactory.Create(new ConfigurationBuilder().Build());

        Skip.IfNot(options.IsConfigured, "GEMINI_API_KEY is not set; the live Gemini suite is skipped.");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLabAiGemini(options);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Test-local cosine. Milestone 3 introduces the production <c>VectorMath</c>; duplicating ten
    /// lines here keeps the live suite independent of code that does not exist yet.
    /// </summary>
    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0;
        double normA = 0;
        double normB = 0;

        for (var i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            normA += (double)a[i] * a[i];
            normB += (double)b[i] * b[i];
        }

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
