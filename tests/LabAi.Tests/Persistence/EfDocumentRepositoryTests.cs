using LabAi.Domain.Entities;
using LabAi.Domain.Enums;
using LabAi.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LabAi.Tests.Persistence;

public sealed class EfDocumentRepositoryTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly LabAiDbContext db;
    private readonly EfDocumentRepository repository;

    public EfDocumentRepositoryTests()
    {
        connection.Open();
        db = new LabAiDbContext(
            new DbContextOptionsBuilder<LabAiDbContext>().UseSqlite(connection).Options);
        db.Database.Migrate();

        // SourceDocument.IngestedByUserId is a FK to users; every seeded document references row 1.
        db.Users.Add(new LabAi.Domain.Entities.User
        {
            Id = 1,
            Username = "ingestor",
            FullName = "Ingestor",
            PasswordHash = "hash",
            PasswordSalt = "salt",
            Role = UserRole.Administrator,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();
        db.ChangeTracker.Clear();

        repository = new EfDocumentRepository(db);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    [Fact]
    public async Task AddAsyncPersistsDocumentAndChunksWithFixedUpIds()
    {
        byte[] blob = [1, 2, 3, 4, 5, 6, 7, 8];

        var documentId = await repository.AddAsync(
            Document(),
            documentId => new[]
            {
                Chunk(documentId, 0, blob),
                Chunk(documentId, 1, blob),
            },
            null);

        documentId.Should().BeGreaterThan(0);
        db.ChangeTracker.Clear();

        var stored = await db.SourceDocuments.SingleAsync(d => d.Id == documentId);
        stored.Status.Should().Be(DocumentStatus.Active);
        stored.SupersededByDocumentId.Should().BeNull();

        var chunks = await db.Chunks.Where(c => c.DocumentId == documentId).OrderBy(c => c.ChunkIndex).ToListAsync();
        chunks.Should().HaveCount(2);
        chunks.Select(c => c.ChunkIndex).Should().Equal(0, 1);
        chunks[0].Embedding.Should().Equal(blob); // byte-exact round-trip through the BLOB column
    }

    [Fact]
    public async Task AddAsyncSupersedesThePreviousVersionInTheSameOperation()
    {
        var previous = Document();
        db.SourceDocuments.Add(previous);
        db.SaveChanges();
        db.ChangeTracker.Clear();

        var documentId = await repository.AddAsync(
            Document(),
            documentId => [Chunk(documentId, 0, [0x00, 0x00, 0x80, 0x3F])],
            previous.Id);

        db.ChangeTracker.Clear();
        var oldRow = await db.SourceDocuments.SingleAsync(d => d.Id == previous.Id);
        oldRow.Status.Should().Be(DocumentStatus.Superseded);
        oldRow.SupersededByDocumentId.Should().Be(documentId);

        var newRow = await db.SourceDocuments.SingleAsync(d => d.Id == documentId);
        newRow.Status.Should().Be(DocumentStatus.Active);
    }

    [Fact]
    public async Task AFailureWhileBuildingChunksLeavesNothingBehind()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddAsync(
            Document(),
            _ => throw new InvalidOperationException("boom"),
            null));

        db.ChangeTracker.Clear();
        // The document row was inserted inside the (uncommitted) transaction; the failure must
        // roll it back, otherwise a crash between the two saves would leave a chunkless document.
        db.SourceDocuments.Should().BeEmpty();
    }

    [Fact]
    public async Task FindActiveByContentHashIgnoresSupersededRows()
    {
        var hash = new string('a', 64);
        db.SourceDocuments.Add(Document(hash: hash, status: DocumentStatus.Superseded));
        var active = Document(hash: hash);
        db.SourceDocuments.Add(active);
        db.SaveChanges();
        db.ChangeTracker.Clear();

        var found = await repository.FindActiveByContentHashAsync(hash);

        found.Should().NotBeNull();
        found!.Id.Should().Be(active.Id);
    }

    [Fact]
    public async Task FindActiveBySourcePathIgnoresSupersededRows()
    {
        db.SourceDocuments.Add(Document(sourcePath: "docs/sop.md", status: DocumentStatus.Superseded));
        var active = Document(sourcePath: "docs/sop.md");
        db.SourceDocuments.Add(active);
        db.SaveChanges();
        db.ChangeTracker.Clear();

        var found = await repository.FindActiveBySourcePathAsync("docs/sop.md");

        found.Should().NotBeNull();
        found!.Id.Should().Be(active.Id);
    }

    private static SourceDocument Document(
        string sourcePath = "docs/sop.md",
        string hash = "hash",
        DocumentStatus status = DocumentStatus.Active) => new()
    {
        SourcePath = sourcePath,
        Title = "SOP",
        Version = 1,
        Kind = DocumentKind.Markdown,
        ContentHash = hash,
        Status = status,
        IngestedAtUtc = DateTime.UtcNow,
        IngestedByUserId = 1,
    };

    private static Chunk Chunk(long documentId, int index, byte[] blob) => new()
    {
        DocumentId = documentId,
        ChunkIndex = index,
        Text = "текст",
        SectionPath = "SOP > 1",
        ContentHash = "chunkhash",
        EmbeddingModelId = "stub-embed-model",
        EmbeddingDimension = 2,
        Embedding = blob,
        CreatedAtUtc = DateTime.UtcNow,
    };
}
