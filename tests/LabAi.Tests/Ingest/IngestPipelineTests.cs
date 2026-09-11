using System.Security.Cryptography;
using System.Text;
using LabAi.Application.Ingest;
using LabAi.Domain.Abstractions;
using LabAi.Domain.Entities;
using LabAi.Domain.Enums;
using LabAi.Domain.ValueObjects;
using NSubstitute;

namespace LabAi.Tests.Ingest;

public sealed class IngestPipelineTests
{
    private const long ActorId = 11;

    private readonly IDocumentParser parser = Substitute.For<IDocumentParser>();
    private readonly IChunkingStrategy strategy = Substitute.For<IChunkingStrategy>();
    private readonly IEmbeddingService embeddings = Substitute.For<IEmbeddingService>();
    private readonly IDocumentRepository repository = Substitute.For<IDocumentRepository>();
    private readonly IVectorStore vectorStore = Substitute.For<IVectorStore>();

    public IngestPipelineTests()
    {
        parser.Kind.Returns(DocumentKind.Markdown);
        strategy.SupportedKinds.Returns([DocumentKind.Markdown]);
        embeddings.ModelId.Returns("stub-embed-model");
        embeddings.EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<IReadOnlyList<string>>()
                .Select(_ => new[] { 3f, 4f })
                .ToList());
    }

    [Fact]
    public async Task DuplicateHashReturnsTheExistingDocumentAndNeverReachesParserOrModel()
    {
        repository.FindActiveByContentHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SourceDocument { Id = 7 });

        var result = await CreatePipeline().IngestAsync(Request());

        result.Should().BeEquivalentTo(new IngestResult(7, IngestOutcome.Duplicate, 0, null));
        parser.DidNotReceive().Parse(Arg.Any<byte[]>());
        await embeddings.DidNotReceive().EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive().AddAsync(
            Arg.Any<SourceDocument>(), Arg.Any<Func<long, IReadOnlyList<Chunk>>>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NewContentIsEmbeddedInBatchesAndStoredAsNormalizedChunks()
    {
        var drafts = Enumerable.Range(0, 5)
            .Select(i => new ChunkDraft("SOP > 4.2", $"text-{i}", LineStart: i + 1, LineEnd: i + 1))
            .ToList();
        parser.Parse(Arg.Any<byte[]>()).Returns(new ParsedDocument([new DocumentSection("SOP > 4.2", "anything")]));
        strategy.Chunk(Arg.Any<ParsedDocument>()).Returns(drafts);

        var capturedBatches = new List<IReadOnlyList<string>>();
        embeddings.EmbedAsync(Arg.Do<IReadOnlyList<string>>(t => capturedBatches.Add(t)), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<IReadOnlyList<string>>()
                .Select(_ => new[] { 3f, 4f })
                .ToList());

        SourceDocument? capturedDocument = null;
        Func<long, IReadOnlyList<Chunk>>? capturedFactory = null;
        repository.AddAsync(
                Arg.Do<SourceDocument>(d => capturedDocument = d),
                Arg.Do<Func<long, IReadOnlyList<Chunk>>>(f => capturedFactory = f),
                Arg.Do<long?>(s => { }),
                Arg.Any<CancellationToken>())
            .Returns(99L);

        var result = await CreatePipeline(embeddingBatchSize: 2).IngestAsync(Request());

        result.Should().BeEquivalentTo(new IngestResult(99, IngestOutcome.Created, 5, null));

        // ceil(5 / 2) = 3 batches of 2, 2 and 1.
        await embeddings.Received(3).EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        capturedBatches.Select(b => b.Count).Should().Equal(2, 2, 1);

        capturedDocument.Should().NotBeNull();
        capturedDocument!.SourcePath.Should().Be("docs/corpus/SOP-QC-001.md");
        capturedDocument.Title.Should().Be("SOP-QC-001");
        capturedDocument.Version.Should().Be(3);
        capturedDocument.Kind.Should().Be(DocumentKind.Markdown);
        capturedDocument.Status.Should().Be(DocumentStatus.Active);
        capturedDocument.ContentHash.Should().Be(Convert.ToHexStringLower(SHA256.HashData([1, 2, 3])));
        capturedDocument.IngestedByUserId.Should().Be(ActorId);

        var chunks = capturedFactory!(99);
        chunks.Should().HaveCount(5);
        chunks.Select(c => c.ChunkIndex).Should().Equal(0, 1, 2, 3, 4);
        chunks.Select(c => c.DocumentId).Should().OnlyContain(id => id == 99);
        chunks.Select(c => c.Text).Should().Equal(drafts.Select(d => d.Text));
        chunks.Select(c => c.ContentHash).Should().Equal(drafts.Select(d =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(d.Text)))));
        chunks.Select(c => c.EmbeddingModelId).Should().OnlyContain(m => m == "stub-embed-model");
        chunks.Select(c => c.EmbeddingDimension).Should().OnlyContain(dim => dim == 2);
        chunks.Select(c => c.Embedding.Length).Should().OnlyContain(length => length == 8);
        chunks.Select(c => c.CreatedAtUtc).Should().OnlyContain(t => t == capturedDocument.IngestedAtUtc);

        var firstDecoded = MemoryMarshalCastToFloats(chunks[0].Embedding);
        firstDecoded[0].Should().BeApproximately(0.6f, 1e-6f);
        firstDecoded[1].Should().BeApproximately(0.8f, 1e-6f);
    }

    [Fact]
    public async Task EmbeddingInputCarriesTheDeterministicContextHeader()
    {
        parser.Parse(Arg.Any<byte[]>()).Returns(ParsedDocument.Empty);
        strategy.Chunk(Arg.Any<ParsedDocument>()).Returns([new ChunkDraft("SOP > 4.2", "chunk text")]);

        IReadOnlyList<string>? firstBatch = null;
        embeddings.EmbedAsync(Arg.Do<IReadOnlyList<string>>(t => firstBatch ??= t), Arg.Any<CancellationToken>())
            .Returns(_ => new List<float[]> { new[] { 1f, 0f } });

        StubAddAsync();

        await CreatePipeline().IngestAsync(Request());

        firstBatch.Should().NotBeNull();
        firstBatch![0].Should().Be("SOP-QC-001 (v3) — SOP > 4.2\nchunk text");
    }

    [Fact]
    public async Task InconsistentVectorDimensionsAbortBeforeAnythingIsPersisted()
    {
        parser.Parse(Arg.Any<byte[]>()).Returns(ParsedDocument.Empty);
        strategy.Chunk(Arg.Any<ParsedDocument>())
            .Returns(Enumerable.Range(0, 4).Select(_ => new ChunkDraft("S", "t")).ToList());

        var callIndex = 0;
        embeddings.EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callIndex++;
                var dimension = callIndex == 1 ? 2 : 3;
                return callInfo.Arg<IReadOnlyList<string>>().Select(_ => new float[dimension]).ToList();
            });

        await FluentActions.Awaiting(() => CreatePipeline(embeddingBatchSize: 2).IngestAsync(Request()))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*inconsistent dimensions*");

        await repository.DidNotReceive().AddAsync(
            Arg.Any<SourceDocument>(), Arg.Any<Func<long, IReadOnlyList<Chunk>>>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangedContentSupersedesThePreviousActiveVersion()
    {
        parser.Parse(Arg.Any<byte[]>()).Returns(ParsedDocument.Empty);
        strategy.Chunk(Arg.Any<ParsedDocument>()).Returns([new ChunkDraft("S", "t")]);

        long? supersededId = null;
        repository.FindActiveBySourcePathAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SourceDocument { Id = 42 });
        repository.AddAsync(
                Arg.Any<SourceDocument>(),
                Arg.Any<Func<long, IReadOnlyList<Chunk>>>(),
                Arg.Do<long?>(s => supersededId = s),
                Arg.Any<CancellationToken>())
            .Returns(100L);

        var result = await CreatePipeline().IngestAsync(Request());

        result.Should().BeEquivalentTo(new IngestResult(100, IngestOutcome.Created, 1, 42));
        supersededId.Should().Be(42);
    }

    [Fact]
    public async Task EmptyDocumentIsStoredWithoutAnyEmbeddingCall()
    {
        parser.Parse(Arg.Any<byte[]>()).Returns(ParsedDocument.Empty);
        strategy.Chunk(Arg.Any<ParsedDocument>()).Returns([]);

        IReadOnlyList<Chunk>? builtChunks = null;
        SourceDocument? capturedDocument = null;
        repository.AddAsync(
                Arg.Do<SourceDocument>(d => capturedDocument = d),
                Arg.Do<Func<long, IReadOnlyList<Chunk>>>(f => builtChunks = f(1)),
                Arg.Do<long?>(s => { }),
                Arg.Any<CancellationToken>())
            .Returns(5L);

        var result = await CreatePipeline().IngestAsync(Request());

        result.Should().BeEquivalentTo(new IngestResult(5, IngestOutcome.Created, 0, null));
        await embeddings.DidNotReceive().EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        builtChunks.Should().BeEmpty();
        capturedDocument!.ContentHash.Should().Be(Convert.ToHexStringLower(SHA256.HashData([1, 2, 3])));
    }

    [Fact]
    public async Task MissingParserForTheKindFailsWithADescriptiveMessage()
    {
        var pipeline = new IngestPipeline([], [strategy], embeddings, repository, vectorStore, 2);

        (await FluentActions.Awaiting(() => pipeline.IngestAsync(Request()))
            .Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Markdown*");
    }

    [Fact]
    public async Task MissingChunkingStrategyForTheKindFailsWithADescriptiveMessage()
    {
        var pipeline = new IngestPipeline([parser], [], embeddings, repository, vectorStore, 2);

        (await FluentActions.Awaiting(() => pipeline.IngestAsync(Request()))
            .Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*chunking strategy*Markdown*");
    }

    [Fact]
    public async Task InvalidRequestsAreRejectedBeforeAnyLookup()
    {
        var pipeline = CreatePipeline();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => pipeline.IngestAsync(Request() with { Version = 0 }));
        await Assert.ThrowsAsync<ArgumentException>(
            () => pipeline.IngestAsync(Request() with { SourcePath = " " }));
        await Assert.ThrowsAsync<ArgumentException>(
            () => pipeline.IngestAsync(Request() with { Title = "" }));

        await repository.DidNotReceive().FindActiveByContentHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private IngestPipeline CreatePipeline(int embeddingBatchSize = 2) =>
        new([parser], [strategy], embeddings, repository, vectorStore, embeddingBatchSize);

    private void StubAddAsync() =>
        repository.AddAsync(
            Arg.Any<SourceDocument>(),
            Arg.Any<Func<long, IReadOnlyList<Chunk>>>(),
            Arg.Any<long?>(),
            Arg.Any<CancellationToken>()).Returns(5L);

    private static IngestRequest Request() => new(
        SourcePath: "docs/corpus/SOP-QC-001.md",
        Title: "SOP-QC-001",
        Version: 3,
        Kind: DocumentKind.Markdown,
        Content: [1, 2, 3],
        IngestedByUserId: ActorId);

    private static float[] MemoryMarshalCastToFloats(byte[] blob) =>
        System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(blob).ToArray();
}
