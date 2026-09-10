using LabAi.Domain.Entities;
using LabAi.Domain.Enums;
using LabAi.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LabAi.Tests.Persistence;

/// <summary>
/// Runs the real migration against a real SQLite database. These tests exist because the mapping
/// decisions are the ones that are expensive to change later: an enum stored as an integer, a
/// <c>DateTime</c> that comes back with <c>Kind=Unspecified</c>, or a cascade that quietly deletes
/// cited chunks are all invisible in the model and obvious in the database.
/// </summary>
public sealed class LabAiDbContextTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly LabAiDbContext db;

    public LabAiDbContextTests()
    {
        // An in-memory SQLite database lives exactly as long as its open connection.
        connection.Open();

        db = new LabAiDbContext(
            new DbContextOptionsBuilder<LabAiDbContext>().UseSqlite(connection).Options);

        // Migrate, not EnsureCreated: this also proves the checked-in migration script applies.
        db.Database.Migrate();

        // One actor exists from the start: every document row cites IngestedByUserId, and the FK is
        // enforced (Restrict), so a document cannot be inserted before the user who ingested it.
        db.Users.Add(NewUser());
        db.SaveChanges();
        db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    [Fact]
    public void Migration_CreatesTheThreeTables_WithSnakeCaseNames()
    {
        var tables = ScalarStrings("SELECT name AS Value FROM sqlite_master WHERE type = 'table'");

        tables.Should().Contain(["users", "source_documents", "chunks"]);
    }

    [Fact]
    public void PersistsEnumsAsTheirNames_SoTheDatabaseStaysReadableByHand()
    {
        var document = NewDocument(kind: DocumentKind.Pdf, status: DocumentStatus.Superseded);
        db.SourceDocuments.Add(document);
        db.SaveChanges();

        ScalarStrings("SELECT Kind AS Value FROM source_documents").Should().Equal("Pdf");
        ScalarStrings("SELECT Status AS Value FROM source_documents").Should().Equal("Superseded");
    }

    [Fact]
    public void ReadsTimestampsBackWithKindUtc()
    {
        // SQLite has no datetime type, so the provider hands back Kind=Unspecified. Anything that
        // later compares or serialises these values would silently drift without the converter.
        db.SourceDocuments.Add(NewDocument());
        db.SaveChanges();
        db.ChangeTracker.Clear();

        var reloaded = db.SourceDocuments.Single();

        reloaded.IngestedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void RoundTripsTheEmbeddingBlob_ByteForByte()
    {
        var bytes = new byte[] { 0x00, 0x00, 0x80, 0x3F, 0xFF, 0x7F, 0x00, 0x00, 0x01 };

        var document = NewDocument();
        db.SourceDocuments.Add(document);
        db.SaveChanges();

        db.Chunks.Add(NewChunk(bytes, documentId: document.Id));
        db.SaveChanges();
        db.ChangeTracker.Clear();

        db.Chunks.Single().Embedding.Should().Equal(bytes);
    }

    [Fact]
    public void RejectsASecondChunkAtTheSamePositionInOneDocument()
    {
        var document = NewDocument();
        db.SourceDocuments.Add(document);
        db.SaveChanges();

        db.Chunks.AddRange(NewChunk(documentId: document.Id, chunkIndex: 0), NewChunk(documentId: document.Id, chunkIndex: 0));

        var act = () => db.SaveChanges();

        act.Should().Throw<DbUpdateException>("a duplicated position means the ingester wrote twice");
    }

    [Fact]
    public void AllowsTheSameChunkIndexInTwoDifferentDocuments()
    {
        var first = NewDocument();
        var second = NewDocument();
        db.SourceDocuments.AddRange(first, second);
        db.SaveChanges();

        db.Chunks.AddRange(NewChunk(documentId: first.Id, chunkIndex: 0), NewChunk(documentId: second.Id, chunkIndex: 0));
        db.SaveChanges();

        db.Chunks.Count().Should().Be(2);
    }

    [Fact]
    public void RefusesToDeleteAUserThatDocumentsStillCite()
    {
        db.SourceDocuments.Add(NewDocument());
        db.SaveChanges();

        // Restrict on a required FK is enforced in the change tracker, so Remove() throws before any
        // SQL is generated — the delete cannot even be queued.
        var act = () => db.Users.Remove(db.Users.Single());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*has been severed*",
                "provenance is the point: the actor who loaded a document must stay resolvable");
    }

    [Fact]
    public void RefusesToDeleteADocumentThatChunksStillCite()
    {
        var document = NewDocument();
        db.SourceDocuments.Add(document);
        db.SaveChanges();
        db.Chunks.Add(NewChunk(documentId: document.Id));
        db.SaveChanges();

        var act = () => db.SourceDocuments.Remove(document);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*has been severed*",
                "an old audit row cites these chunks; deleting them invalidates evidence still on screen");
    }

    [Fact]
    public void RawSqlCannotDeleteAUserThatDocumentsStillCite()
    {
        db.SourceDocuments.Add(NewDocument());
        db.SaveChanges();

        // The tracker-level refusal above is bypassed by raw SQL, so the database must hold the line
        // on its own. This also proves foreign-key enforcement is actually switched on for the
        // connection: with PRAGMA foreign_keys off, SQLite would happily orphan the documents.
        var act = () => db.Database.ExecuteSqlRaw("DELETE FROM users WHERE Id = 1");

        act.Should().Throw<SqliteException>().WithMessage("*FOREIGN KEY constraint failed*");
    }

    [Fact]
    public void RawSqlCannotDeleteADocumentThatChunksStillCite()
    {
        var document = NewDocument();
        db.SourceDocuments.Add(document);
        db.SaveChanges();
        db.Chunks.Add(NewChunk(documentId: document.Id));
        db.SaveChanges();

        var act = () => db.Database.ExecuteSqlRaw("DELETE FROM source_documents WHERE Id = 1");

        act.Should().Throw<SqliteException>().WithMessage("*FOREIGN KEY constraint failed*");
    }

    [Fact]
    public void KeepsTheSupersedeChain_WithoutACascadeBackIntoHistory()
    {
        var original = NewDocument(version: 1, status: DocumentStatus.Active);
        db.SourceDocuments.Add(original);
        db.SaveChanges();

        var replacement = NewDocument(version: 2, status: DocumentStatus.Active);
        db.SourceDocuments.Add(replacement);
        db.SaveChanges();

        original.Status = DocumentStatus.Superseded;
        original.SupersededByDocumentId = replacement.Id;
        db.SaveChanges();
        db.ChangeTracker.Clear();

        var reloaded = db.SourceDocuments.OrderBy(d => d.Version).ToList();
        reloaded.Should().HaveCount(2, "re-ingesting replaces the active version, it never erases the old one");
        reloaded[0].Status.Should().Be(DocumentStatus.Superseded);
        reloaded[0].SupersededByDocumentId.Should().Be(replacement.Id);
        reloaded[1].Status.Should().Be(DocumentStatus.Active);
    }

    [Fact]
    public void DesignTimeFactory_PointsAtAThrowawayDatabase_NotTheLiveOne()
    {
        // Generating or applying a migration must never be able to reach the corpus and the audit journal.
        using var context = new LabAiDbContextFactory().CreateDbContext([]);

        var connectionString = context.Database.GetConnectionString();

        connectionString.Should().Contain("design-time.db");
        connectionString.Should().NotContain("lab.db");
    }

    private List<string> ScalarStrings(string sql) => db.Database.SqlQueryRaw<string>(sql).ToList();

    private static User NewUser() => new()
    {
        Username = "analyst",
        FullName = "Demo Analyst",
        PasswordHash = "hash",
        PasswordSalt = "salt",
        Role = UserRole.Analyst,
        IsActive = true,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static SourceDocument NewDocument(
        int version = 1,
        DocumentKind kind = DocumentKind.Markdown,
        DocumentStatus status = DocumentStatus.Active) => new()
    {
        SourcePath = "docs/corpus/SOP-QC-001.md",
        Title = "HPLC system suitability",
        Version = version,
        Kind = kind,
        ContentHash = new string('a', 64),
        Status = status,
        IngestedAtUtc = new DateTime(2026, 9, 10, 6, 30, 0, DateTimeKind.Utc),
        IngestedByUserId = 1
    };

    private static Chunk NewChunk(byte[]? embedding = null, long documentId = 1, int chunkIndex = 0) => new()
    {
        DocumentId = documentId,
        ChunkIndex = chunkIndex,
        Text = "Acceptance criteria: RSD must not exceed 2.0%.",
        SectionPath = "h1 > h2",
        PageStart = null,
        PageEnd = null,
        LineStart = 1,
        LineEnd = 4,
        ContentHash = new string('b', 64),
        EmbeddingModelId = "gemini-embedding-001",
        EmbeddingDimension = embedding?.Length ?? 4,
        Embedding = embedding ?? [1, 2, 3, 4],
        CreatedAtUtc = new DateTime(2026, 9, 10, 6, 30, 0, DateTimeKind.Utc)
    };
}
