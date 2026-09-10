using LabAi.Application.Auth;
using LabAi.Domain.Abstractions;
using LabAi.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LabAi.Infrastructure.Persistence;

/// <summary>
/// Registers persistence and the services that sit directly on it. The connection string is passed in
/// rather than read from <c>IConfiguration</c> here, so Infrastructure stays free of the configuration
/// abstraction and the composition root remains the only place that knows where settings come from.
/// </summary>
/// <remarks>
/// <c>IAuthService</c> is an Application service registered from Infrastructure, which looks like a
/// layering smell and is not: the only reason it lives here is that its <see cref="IUserStore"/> is
/// EF-backed, and splitting the registration across two extensions would hide that coupling.
/// </remarks>
public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddLabAiPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string must not be blank.", nameof(connectionString));

        services.AddDbContext<LabAiDbContext>(options => options.UseSqlite(connectionString));

        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IUserStore, EfUserStore>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<DbSeeder>();

        return services;
    }
}
