using LabAi.Application.Chunking;
using LabAi.Application.Ingest;
using LabAi.Domain.Abstractions;
using LabAi.Domain.Enums;
using LabAi.Domain.ValueObjects;
using LabAi.Infrastructure.Parsing;
using LabAi.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LabAi.Tests.Ingest;

/// <summary>
/// Milestone 2 DoD: ingesting the authored corpus must produce a stable chunk count, pinned
/// per file. The pipeline here is fully real (parsers, chunkers, repository on a migrated
/// in-memory SQLite) except embeddings, which are a constant stub — chunking is a pure function
/// of document text, so the counts document the deliberate shape of the corpus and any chunker
/// change that silently shifts retrieval behaviour fails here.
/// </summary>
public sealed class CorpusIngestTests : IDisposable
{
    private const int MaxChunkChars = 2000;
    private const int OverlapChars = 200;

    private static readonly CorpusFile[] Corpus =
    [
        new("SOP-QC-001-hplc-system-suitability.md", DocumentKind.Markdown, "SOP-QC-001 Системная пригодность ВЭЖХ", 3, 6),
        new("SOP-QC-002-analytical-balance.md", DocumentKind.Markdown, "SOP-QC-002 Ежедневная проверка аналитических весов", 2, 7),
        new("SOP-QC-003-ph-meter-calibration.md", DocumentKind.Markdown, "SOP-QC-003 Калибровка pH-метра", 2, 6),
        new("SOP-QC-004-pipette-qc.md", DocumentKind.Markdown, "SOP-QC-004 Квартальная проверка дозаторов", 1, 6),
        new("SOP-QA-005-oos-investigation.md", DocumentKind.Markdown, "SOP-QA-005 Расследование результатов вне спецификации (OOS)", 1, 6),
        new(Path.Combine("instrument", "hplc-run-001.csv"), DocumentKind.Csv, "hplc-run-001 Хроматограмма USP Method 1", 1, 2),
        new(Path.Combine("instrument", "balance-log-2026-09.json"), DocumentKind.Json, "balance-log-2026-09 Журнал проверки весов", 1, 1),
    ];

    private static readonly string corpusDirectory = FindCorpusDirectory();

    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly LabAiDbContext db;
    private readonly long userId;
    private readonly IngestPipeline pipeline;

    public CorpusIngestTests()
    {
        connection.Open();
        db = new LabAiDbContext(
            new DbContextOptionsBuilder<LabAiDbContext>().UseSqlite(connection).Options);
        db.Database.Migrate();

        db.Users.Add(new LabAi.Domain.Entities.User
        {
            Username = "ingestor",
            FullName = "Corpus Ingestor",
            PasswordHash = "hash",
            PasswordSalt = "salt",
            Role = UserRole.Analyst,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();
        userId = db.Users.Single().Id;
        db.ChangeTracker.Clear();

        pipeline = new IngestPipeline(
            new IDocumentParser[] { new MarkdownDocumentParser(), new InstrumentCsvParser(), new JsonDocumentParser() },
            new IChunkingStrategy[]
            {
                new MarkdownChunker(MaxChunkChars, OverlapChars),
                new CsvRowGroupChunker(MaxChunkChars),
                new GenericTextChunker(MaxChunkChars, OverlapChars)
            },
            new ConstantEmbeddings(),
            new EfDocumentRepository(db),
            embeddingBatchSize: 16);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    [Fact]
    public async Task IngestsTheWholeCorpusWithAStableChunkCount()
    {
        foreach (var file in Corpus)
        {
            var result = await IngestAsync(file);

            result.Outcome.Should().Be(IngestOutcome.Created, file.RelativePath);
            result.ChunksCreated.Should().Be(file.ExpectedChunks, file.RelativePath);
            result.SupersededDocumentId.Should().BeNull();

            var document = await db.SourceDocuments.AsNoTracking().SingleAsync(d => d.Id == result.DocumentId);
            document.Title.Should().Be(file.Title);
            document.Version.Should().Be(file.Version);
            document.Status.Should().Be(DocumentStatus.Active);
        }
    }

    [Fact]
    public async Task ReingestingTheWholeCorpusProducesOnlyDuplicates()
    {
        var firstPassIds = new List<long>();
        foreach (var file in Corpus)
        {
            var result = await IngestAsync(file);
            firstPassIds.Add(result.DocumentId);
        }

        var totalChunksBefore = await db.Chunks.CountAsync();
        totalChunksBefore.Should().Be(Corpus.Sum(f => f.ExpectedChunks));

        foreach (var file in Corpus)
        {
            var result = await IngestAsync(file);

            result.Outcome.Should().Be(IngestOutcome.Duplicate, file.RelativePath);
            result.ChunksCreated.Should().Be(0);
            firstPassIds.Should().Contain(result.DocumentId);
        }

        (await db.Chunks.CountAsync()).Should().Be(totalChunksBefore);
        (await db.SourceDocuments.CountAsync()).Should().Be(Corpus.Length);
    }

    [Fact]
    public async Task MarkdownAndCsvChunksCarryRetrievableCoordinates()
    {
        foreach (var file in Corpus)
            await IngestAsync(file);

        var markdownChunks = await db.SourceDocuments
            .Where(d => d.Kind == DocumentKind.Markdown)
            .Join(db.Chunks, d => d.Id, c => c.DocumentId, (d, c) => c)
            .ToListAsync();

        markdownChunks.Should().NotBeEmpty();
        markdownChunks.Should().OnlyContain(c => c.SectionPath.Length > 0);
        markdownChunks.Should().OnlyContain(c => c.LineStart.HasValue && c.LineEnd.HasValue);

        var csvChunkTexts = await db.SourceDocuments
            .Where(d => d.Kind == DocumentKind.Csv)
            .Join(db.Chunks, d => d.Id, c => c.DocumentId, (d, c) => c.Text)
            .ToListAsync();

        csvChunkTexts.Should().HaveCount(2);
        csvChunkTexts.Should().OnlyContain(t => t.StartsWith("Результаты измерений, образец Sample_2026-"));
    }

    private async Task<IngestResult> IngestAsync(CorpusFile file)
    {
        var content = await File.ReadAllBytesAsync(Path.Combine(corpusDirectory, file.RelativePath));
        return await pipeline.IngestAsync(
            new IngestRequest(file.RelativePath, file.Title, file.Version, file.Kind, content, userId));
    }

    private static string FindCorpusDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "docs", "corpus")))
            directory = directory.Parent;

        if (directory is null)
            throw new InvalidOperationException("docs/corpus not found above the test assembly directory.");

        return Path.Combine(directory.FullName, "docs", "corpus");
    }

    private sealed record CorpusFile(string RelativePath, DocumentKind Kind, string Title, int Version, int ExpectedChunks);

    private sealed class ConstantEmbeddings : IEmbeddingService
    {
        public string ModelId => "stub-embedding";

        public Task<IReadOnlyList<float[]>> EmbedAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<float[]> vectors = texts
                .Select(_ => new float[] { 0.6f, 0.8f, 0f, 0f })
                .ToArray();
            return Task.FromResult(vectors);
        }
    }
}
