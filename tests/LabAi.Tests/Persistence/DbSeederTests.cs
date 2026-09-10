using LabAi.Domain.Enums;
using LabAi.Infrastructure.Persistence;
using LabAi.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LabAi.Tests.Persistence;

/// <summary>
/// Seeding runs on every host start, so its failure modes are chronic, not acute: re-hashing an
/// existing account, printing a configured secret, or a second admin account appearing out of
/// nowhere would each look fine in a single run and corrupt the story over a week of restarts.
/// </summary>
public sealed class DbSeederTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly LabAiDbContext db;
    private readonly DbSeeder seeder;

    public DbSeederTests()
    {
        connection.Open();
        db = new LabAiDbContext(
            new DbContextOptionsBuilder<LabAiDbContext>().UseSqlite(connection).Options);
        db.Database.Migrate();
        seeder = new DbSeeder(db, new PasswordHasher());
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    [Fact]
    public async Task SeedsOneAccountPerRole_WithTheDemoNames()
    {
        var result = await seeder.SeedAsync(demoPassword: "demo123");

        var names = result.Created.Select(u => u.Username).ToList();
        names.Should().Equal(["admin", "analyst", "operator"]);
    }

    [Fact]
    public async Task SeedsAccountsThatActuallyLogIn()
    {
        await seeder.SeedAsync(demoPassword: "demo123");
        var hasher = new PasswordHasher();
        db.ChangeTracker.Clear();

        var analyst = db.Users.Single(u => u.Username == "analyst");

        analyst.Role.Should().Be(UserRole.Analyst);
        analyst.IsActive.Should().BeTrue();
        hasher.Verify("demo123", analyst.PasswordHash, analyst.PasswordSalt).Should().BeTrue();
    }

    [Fact]
    public async Task NeverStoresThePlaintextPassword()
    {
        await seeder.SeedAsync(demoPassword: "demo123");
        db.ChangeTracker.Clear();

        db.Users.Select(u => u.PasswordHash)
            .Should().NotContain("demo123", "the hash column is what a dump would exfiltrate");
    }

    [Fact]
    public async Task IsIdempotent_ASecondPassCreatesNothingAndTouchesNobody()
    {
        await seeder.SeedAsync(demoPassword: "demo123");
        var before = db.Users.OrderBy(u => u.Username)
            .Select(u => new { u.Username, u.PasswordHash, u.PasswordSalt })
            .ToList();
        db.ChangeTracker.Clear();

        var second = await seeder.SeedAsync(demoPassword: "demo123");

        second.Created.Should().BeEmpty();
        db.Users.OrderBy(u => u.Username)
            .Select(u => new { u.Username, u.PasswordHash, u.PasswordSalt })
            .ToList()
            .Should().Equal(before, "a row already in the database is a record, not a template");
    }

    [Fact]
    public async Task DetectsExistingAccountsCaseInsensitively_SoRestartCannotCreateAnAdmin()
    {
        // A pre-existing "Admin" (any case) must suppress the "admin" seed: on a restart against a
        // populated database the OrdinalIgnoreCase comparison is the only thing standing between
        // the seeder and a second administrator account.
        db.Users.Add(new LabAi.Domain.Entities.User
        {
            Username = "Admin",
            FullName = "Pre-existing",
            PasswordHash = "hash",
            PasswordSalt = "salt",
            Role = UserRole.Administrator,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();

        var result = await seeder.SeedAsync(demoPassword: "demo123");

        result.Created.Should().NotContain(u => u.Username == "admin");
        db.Users.Count(u => u.Role == UserRole.Administrator).Should().Be(1);
        db.Users.Single(u => u.Role == UserRole.Administrator).FullName.Should().Be("Pre-existing",
            "existing rows are records; the seeder must not rewrite them");
    }

    [Fact]
    public async Task ReturnsGeneratedPasswords_InTheResultOnly()
    {
        var result = await seeder.SeedAsync(demoPassword: null);

        result.Created.Should().OnlyContain(u => !string.IsNullOrEmpty(u.Password));
    }

    [Fact]
    public async Task DoesNotEchoAConfiguredPassword()
    {
        var result = await seeder.SeedAsync(demoPassword: "demo123");

        result.Created.Should().OnlyContain(u => u.Password == null,
            "whoever configured the password already knows it; echoing it puts a secret in the startup output");
    }

    [Fact]
    public async Task GeneratedPasswords_ExcludeConsoleAmbiguousCharacters()
    {
        var result = await seeder.SeedAsync(demoPassword: null);

        // l 1 I O 0 o are unreadable on a printed console and the whole point is reading it once.
        foreach (var ambiguous in new[] { 'l', '1', 'I', 'O', '0', 'o' })
        {
            result.Created.Select(u => u.Password!)
                .Should().NotContain(p => p.Contains(ambiguous), $"'{ambiguous}' is ambiguous");
        }
    }
}
