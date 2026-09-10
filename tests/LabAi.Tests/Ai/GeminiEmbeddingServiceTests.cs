using LabAi.Infrastructure.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LabAi.Tests.Ai;

/// <summary>
/// Locks the embedding adapter contract. The two assertions that matter most for data integrity are
/// "one vector per input, in order" — a silent reordering would attribute embeddings to the wrong
/// chunk — and "no per-call generation options", which is what stops the store drifting into two
/// incomparable vector spaces (ADR-0002, decision 4).
/// </summary>
public sealed class GeminiEmbeddingServiceTests
{
    private static readonly GeminiOptions Configured = new() { ApiKey = "not-a-real-key" };

    [Fact]
    public async Task ThrowsNotConfigured_AndNeverTouchesTheConnector_WhenTheKeyIsMissing()
    {
        var connectorResolved = false;
        var service = new GeminiEmbeddingService(
            new GeminiOptions(),
            () =>
            {
                connectorResolved = true;
                return Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
            },
            NullLogger<GeminiEmbeddingService>.Instance);

        var act = () => service.EmbedAsync(["some text"]);

        await act.Should().ThrowAsync<GeminiNotConfiguredException>();
        connectorResolved.Should().BeFalse("the adapter must fail before resolving the alpha connector");
    }

    [Fact]
    public async Task ReturnsNothing_WithoutCallingTheModel_WhenGivenNoTexts()
    {
        var called = false;
        var service = ServiceWith(GeneratorReturning(
            texts: _ => called = true,
            vectors: []));

        var result = await service.EmbedAsync([]);

        result.Should().BeEmpty();
        called.Should().BeFalse("an empty batch is not worth a paid API round trip");
    }

    [Fact]
    public async Task ReturnsOneVectorPerInput_InInputOrder()
    {
        var service = ServiceWith(GeneratorReturning(vectors:
        [
            [1f, 0f, 0f],
            [0f, 2f, 0f],
            [0f, 0f, 3f],
        ]));

        var result = await service.EmbedAsync(["first", "second", "third"]);

        result.Should().HaveCount(3);
        result[0].Should().Equal(1f, 0f, 0f);
        result[1].Should().Equal(0f, 2f, 0f);
        result[2].Should().Equal(0f, 0f, 3f);
    }

    [Fact]
    public async Task ForwardsTheTextsToTheModelUnchanged()
    {
        List<string>? forwarded = null;
        var service = ServiceWith(GeneratorReturning(
            texts: captured => forwarded = captured,
            vectors: [[0f], [0f]]));

        await service.EmbedAsync(["alpha", "beta"]);

        forwarded.Should().Equal("alpha", "beta");
    }

    [Fact]
    public async Task PassesNoGenerationOptions_SoTheModelCannotDriftPerCall()
    {
        var modelCalled = false;
        EmbeddingGenerationOptions? options = new();
        var service = ServiceWith(GeneratorReturning(
            capturedOptions: captured =>
            {
                modelCalled = true;
                options = captured;
            },
            vectors: [[0f]]));

        await service.EmbedAsync(["alpha"]);

        modelCalled.Should().BeTrue();
        options.Should().BeNull("a per-call model override is how a store ends up mixing vector spaces");
    }

    [Fact]
    public async Task Throws_WhenTheModelReturnsTheWrongNumberOfVectors()
    {
        var service = ServiceWith(GeneratorReturning(vectors: [[1f, 2f]]));

        var act = () => service.EmbedAsync(["first", "second"]);

        var exception = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;

        exception.Message.Should().Contain("2 inputs").And.Contain("1 embeddings");
    }

    [Fact]
    public void ExposesTheConfiguredModelId()
    {
        var service = new GeminiEmbeddingService(
            new GeminiOptions { ApiKey = "k", EmbeddingModelId = "gemini-embedding-001" },
            () => Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>(),
            NullLogger<GeminiEmbeddingService>.Instance);

        service.ModelId.Should().Be("gemini-embedding-001");
    }

    [Fact]
    public async Task PropagatesTheCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken token = default;
        var service = ServiceWith(GeneratorReturning(
            capturedToken: captured => token = captured,
            vectors: [[0f]]));

        await service.EmbedAsync(["alpha"], cts.Token);

        token.Should().Be(cts.Token);
    }

    private static GeminiEmbeddingService ServiceWith(IEmbeddingGenerator<string, Embedding<float>> generator) =>
        new(Configured, () => generator, NullLogger<GeminiEmbeddingService>.Instance);

    private static IEmbeddingGenerator<string, Embedding<float>> GeneratorReturning(
        float[][] vectors,
        Action<List<string>>? texts = null,
        Action<EmbeddingGenerationOptions?>? capturedOptions = null,
        Action<CancellationToken>? capturedToken = null)
    {
        var generator = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
        generator.GenerateAsync(
                Arg.Do<IEnumerable<string>>(captured => texts?.Invoke(captured.ToList())),
                Arg.Do<EmbeddingGenerationOptions>(captured => capturedOptions?.Invoke(captured)),
                Arg.Do<CancellationToken>(captured => capturedToken?.Invoke(captured)))
            .Returns(new GeneratedEmbeddings<Embedding<float>>(
                vectors.Select(vector => new Embedding<float>(vector)).ToList()));
        return generator;
    }
}
