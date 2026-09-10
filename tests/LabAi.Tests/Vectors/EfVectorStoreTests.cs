using LabAi.Application.Vectors;
using LabAi.Domain.Abstractions;
using LabAi.Domain.Entities;
using LabAi.Domain.Enums;
using LabAi.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LabAi.Tests.Vectors;

/// <summary>
/// Milestone 3 DoD: the EF-backed vector store must encode/decode BLOBs byte-exactly, exclude
/// superseded documents from the snapshot, refuse to build when embeddings come from multiple
/// model/dimension pairs, and publish a new immutable snapshot atomically via Volatile.Write.
/// </summary>
public sealed class EfVectorStoreTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly LabAiDbContext db;
    private readonly IDbContextFactory<LabAiDbContext> contextFactory;

    public EfVectorStoreTests()
    {
        connection.Open();
        db = new LabAiDbContext(
            new DbContextOptionsBuilder<LabAiDbContext>().UseSqlite(connection).Options);
        db.Database.Migrate();

        // Seed a test user so FK constraints are satisfied.
        db.Users.Add(new User
        {
            Username = "test",
            FullName = "Test User",
            PasswordHash = "hash",
            PasswordSalt = "salt",
            Role = UserRole.Analyst,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();

        var options = new DbContextOptionsBuilder<LabAiDbContext>()
            .UseSqlite(connection)
            .Options;
        contextFactory = new TestDbContextFactory(options);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    [Fact]
    public async Task RebuildEncodesAndDecodesBlobsByteExactly()
    {
        var doc = CreateDocument("doc1", DocumentStatus.Active);
        db.SourceDocuments.Add(doc);
        db.SaveChanges();

        var floats = Enumerable.Range(0, 6).Select(i => (float)i).ToArray(); // dimension 6
        var blob = VectorMath.ToFloat32Blob(floats);

        db.Chunks.Add(new Chunk
        {
            DocumentId = doc.Id,
            ChunkIndex = 0,
            Text = "test chunk",
            SectionPath = "1. Test",
            ContentHash = "abc123",
            EmbeddingModelId = "test-model",
            EmbeddingDimension = 6,
            Embedding = blob,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();

        var store = new EfVectorStore(contextFactory);
        await store.RebuildAsync();

        var snapshot = store.Snapshot;
        snapshot.Count.Should().Be(1);
        snapshot.Dimension.Should().Be(6);
        snapshot.ChunkIds[0].Should().NotBe(0);
        snapshot.DocumentIds[0].Should().Be(doc.Id);

        // Verify round-trip: the flat buffer contains exactly our floats.
        var decoded = new float[6];
        Array.Copy(snapshot.Vectors, 0, decoded, 0, 6);
        decoded.Should().Equal(floats);
    }

    [Fact]
    public async Task SupersededDocumentsAreExcludedFromSnapshot()
    {
        var oldDoc = CreateDocument("old", DocumentStatus.Superseded);
        var newDoc = CreateDocument("new", DocumentStatus.Active);
        db.SourceDocuments.AddRange(oldDoc, newDoc);
        db.SaveChanges();

        var blob = VectorMath.ToFloat32Blob([1f, 2f]);

        db.Chunks.Add(new Chunk
        {
            DocumentId = oldDoc.Id,
            ChunkIndex = 0,
            Text = "old chunk",
            SectionPath = "1. Old",
            ContentHash = "old-hash",
            EmbeddingModelId = "test-model",
            EmbeddingDimension = 2,
            Embedding = blob,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.Chunks.Add(new Chunk
        {
            DocumentId = newDoc.Id,
            ChunkIndex = 0,
            Text = "new chunk",
            SectionPath = "1. New",
            ContentHash = "new-hash",
            EmbeddingModelId = "test-model",
            EmbeddingDimension = 2,
            Embedding = blob,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();

        var store = new EfVectorStore(contextFactory);
        await store.RebuildAsync();

        var snapshot = store.Snapshot;
        snapshot.Count.Should().Be(1);
        snapshot.DocumentIds[0].Should().Be(newDoc.Id);
    }

    [Fact]
    public async Task MixedDimensionsCauseRebuildToFail()
    {
        var doc1 = CreateDocument("doc1", DocumentStatus.Active);
        var doc2 = CreateDocument("doc2", DocumentStatus.Active);
        db.SourceDocuments.AddRange(doc1, doc2);
        db.SaveChanges();

        db.Chunks.Add(new Chunk
        {
            DocumentId = doc1.Id,
            ChunkIndex = 0,
            Text = "chunk1",
            SectionPath = "1. A",
            ContentHash = "hash1",
            EmbeddingModelId = "model-a",
            EmbeddingDimension = 2,
            Embedding = VectorMath.ToFloat32Blob([1f, 2f]),
            CreatedAtUtc = DateTime.UtcNow
        });
        db.Chunks.Add(new Chunk
        {
            DocumentId = doc2.Id,
            ChunkIndex = 0,
            Text = "chunk2",
            SectionPath = "1. B",
            ContentHash = "hash2",
            EmbeddingModelId = "model-b",
            EmbeddingDimension = 4,
            Embedding = VectorMath.ToFloat32Blob([1f, 2f, 3f, 4f]),
            CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();

        var store = new EfVectorStore(contextFactory);

        (await FluentActions.Awaiting(() => store.RebuildAsync())
            .Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*model/dimension*");
    }

    [Fact]
    public async Task SnapshotSwapIsAtomic()
    {
        var doc = CreateDocument("doc", DocumentStatus.Active);
        db.SourceDocuments.Add(doc);
        db.SaveChanges();

        var blob = VectorMath.ToFloat32Blob([1f, 2f, 3f]);
        db.Chunks.Add(new Chunk
        {
            DocumentId = doc.Id,
            ChunkIndex = 0,
            Text = "chunk",
            SectionPath = "1. Test",
            ContentHash = "hash",
            EmbeddingModelId = "test",
            EmbeddingDimension = 3,
            Embedding = blob,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();

        var store = new EfVectorStore(contextFactory);

        // Before rebuild: empty snapshot.
        store.Snapshot.Count.Should().Be(0);
        store.Snapshot.Dimension.Should().Be(0);

        await store.RebuildAsync();

        // After rebuild: populated snapshot, read atomically.
        var snapshot1 = store.Snapshot;
        snapshot1.Count.Should().Be(1);
        snapshot1.Dimension.Should().Be(3);

        // Second read returns the same reference (immutable snapshot, no reallocation).
        var snapshot2 = store.Snapshot;
        ReferenceEquals(snapshot1, snapshot2).Should().BeTrue();
    }

    [Fact]
    public async Task EmptyStoreProducesEmptySnapshot()
    {
        var store = new EfVectorStore(contextFactory);
        await store.RebuildAsync();

        var snapshot = store.Snapshot;
        snapshot.Count.Should().Be(0);
        snapshot.Dimension.Should().Be(0);
        snapshot.ChunkIds.Should().BeEmpty();
        snapshot.DocumentIds.Should().BeEmpty();
        snapshot.Vectors.Should().BeEmpty();
    }

    private static SourceDocument CreateDocument(string title, DocumentStatus status) => new()
    {
        Title = title,
        SourcePath = $"docs/{title}.md",
        Version = 1,
        Kind = DocumentKind.Markdown,
        ContentHash = Guid.NewGuid().ToString("N"),
        Status = status,
        IngestedAtUtc = DateTime.UtcNow,
        IngestedByUserId = 1
    };

    private sealed class TestDbContextFactory(DbContextOptions<LabAiDbContext> options) : IDbContextFactory<LabAiDbContext>
    {
        public LabAiDbContext CreateDbContext() => new(options);
        public ValueTask<LabAiDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateDbContext());
    }
}
