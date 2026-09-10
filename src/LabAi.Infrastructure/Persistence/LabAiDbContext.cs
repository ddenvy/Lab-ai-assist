using LabAi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LabAi.Infrastructure.Persistence;

/// <summary>
/// SQLite-backed persistence context for the RAG corpus and the identity that audit rows point at.
/// </summary>
public class LabAiDbContext(DbContextOptions<LabAiDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<SourceDocument> SourceDocuments => Set<SourceDocument>();
    public DbSet<Chunk> Chunks => Set<Chunk>();
    public DbSet<AiAuditEntry> AiAuditEntries => Set<AiAuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LabAiDbContext).Assembly);
    }
}
