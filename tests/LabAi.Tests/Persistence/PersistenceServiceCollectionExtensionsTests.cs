using LabAi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LabAi.Tests.Persistence;

public sealed class PersistenceServiceCollectionExtensionsTests
{
    [Fact]
    public void RegistersTheContextAsScoped_NotAsASingleton()
    {
        // A singleton DbContext in a web host is a concurrency bug waiting for a second browser tab:
        // the change tracker is shared and not thread-safe. Scoped means one context per request.
        var services = new ServiceCollection();

        services.AddLabAiPersistence("DataSource=:memory:");

        services.Single(d => d.ServiceType == typeof(LabAiDbContext))
            .Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void ResolvesAContextCarryingTheGivenConnectionString()
    {
        var services = new ServiceCollection();
        services.AddLabAiPersistence("DataSource=:memory:");

        using var provider = services.BuildServiceProvider();
        using var db = provider.GetRequiredService<LabAiDbContext>();

        db.Database.GetConnectionString().Should().Be("DataSource=:memory:");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsABlankConnectionString(string? connectionString)
    {
        // Fail at startup, not on the first query: a blank connection string otherwise produces a
        // SQLite database file named after the empty string in the process working directory.
        var act = () => new ServiceCollection().AddLabAiPersistence(connectionString!);

        act.Should().Throw<ArgumentException>().WithParameterName("connectionString");
    }
}
