using LabAi.Domain.Enums;
using LabAi.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LabAi.Tests.Persistence;

public sealed class EfUserStoreTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly LabAiDbContext db;
    private readonly EfUserStore store;

    public EfUserStoreTests()
    {
        connection.Open();
        db = new LabAiDbContext(
            new DbContextOptionsBuilder<LabAiDbContext>().UseSqlite(connection).Options);
        db.Database.Migrate();

        db.Users.Add(new LabAi.Domain.Entities.User
        {
            Username = "analyst",
            FullName = "Demo Analyst",
            PasswordHash = "hash",
            PasswordSalt = "salt",
            Role = UserRole.Analyst,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();
        db.ChangeTracker.Clear();

        store = new EfUserStore(db);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    [Fact]
    public async Task FindsAnExistingUserByUsername()
    {
        var user = await store.FindByUsernameAsync("analyst");

        user.Should().NotBeNull();
        user!.FullName.Should().Be("Demo Analyst");
    }

    [Fact]
    public async Task ReturnsNullForAnUnknownUsername()
    {
        (await store.FindByUsernameAsync("ghost")).Should().BeNull();
    }

    [Fact]
    public async Task LookupIsCaseSensitive_MatchingTheAuthServiceContract()
    {
        // Deliberate Mini-CDS parity: SQLite's LIKE would fold case, but the LINQ translation is
        // a plain equality, and login errors are generic anyway — the failure mode of a mis-cased
        // username is "Invalid username or password.", which is exactly the desired opacity.
        (await store.FindByUsernameAsync("Analyst")).Should().BeNull();
    }

    [Fact]
    public async Task ReturnsAnUntrackedEntity_SoLoginCannotAccidentallyPersistIt()
    {
        var user = await store.FindByUsernameAsync("analyst");

        db.Entry(user!).State.Should().Be(EntityState.Detached);
    }
}
