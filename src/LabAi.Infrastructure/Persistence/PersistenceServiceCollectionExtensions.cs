using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LabAi.Infrastructure.Persistence;

/// <summary>
/// Registers persistence for the host. The connection string is passed in rather than read from
/// <c>IConfiguration</c> here, so Infrastructure stays free of the configuration abstraction and the
/// composition root remains the only place that knows where settings come from.
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddLabAiPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string must not be blank.", nameof(connectionString));

        services.AddDbContext<LabAiDbContext>(options => options.UseSqlite(connectionString));

        return services;
    }
}
