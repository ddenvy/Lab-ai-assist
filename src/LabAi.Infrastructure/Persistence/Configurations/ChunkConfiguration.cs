using LabAi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabAi.Infrastructure.Persistence.Configurations;

public class ChunkConfiguration : IEntityTypeConfiguration<Chunk>
{
    public void Configure(EntityTypeBuilder<Chunk> builder)
    {
        builder.ToTable("chunks");
        builder.HasKey(c => c.Id);

        // No HasMaxLength on Text, deliberately: SQLite TEXT is unbounded, and an artificial cap here
        // would not protect anything — it would only turn a legitimately long section into a save-time
        // failure once the chunk budget in appsettings is raised.
        builder.Property(c => c.Text).IsRequired();
        builder.Property(c => c.SectionPath).HasMaxLength(512).IsRequired();
        builder.Property(c => c.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(c => c.EmbeddingModelId).HasMaxLength(64).IsRequired();
        builder.Property(c => c.ChunkIndex).IsRequired();
        builder.Property(c => c.EmbeddingDimension).IsRequired();

        // Raw float32 blob, mapped to BLOB by default. Length is validated by the vector store, not here.
        builder.Property(c => c.Embedding).IsRequired();

        builder.Property(c => c.CreatedAtUtc)
            .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        // Restrict, not Cascade: a document's chunks are the citations old audit rows point at, so
        // removing them would silently invalidate evidence that is still on screen.
        builder.HasOne(c => c.Document)
            .WithMany()
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        // A chunk is identified by its document and position; duplicates mean the ingester double-wrote.
        builder.HasIndex(c => new { c.DocumentId, c.ChunkIndex }).IsUnique();

        // Startup guard reads SELECT DISTINCT EmbeddingModelId, EmbeddingDimension over the whole table.
        builder.HasIndex(c => new { c.EmbeddingModelId, c.EmbeddingDimension });
    }
}
