using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LabAi.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for EF Core tooling (migrations add, database update). Not used at runtime —
/// the web host builds options from <c>ConnectionStrings:LabDb</c> via DI.
/// </summary>
/// <remarks>
/// Points at a throwaway database so generating or applying a migration can never touch
/// <c>data/lab.db</c>, which holds the ingested corpus and the audit journal.
/// </remarks>
public sealed class LabAiDbContextFactory : IDesignTimeDbContextFactory<LabAiDbContext>
{
    public LabAiDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LabAiDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;

        return new LabAiDbContext(options);
    }
}
