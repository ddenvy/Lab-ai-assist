using LabAi.Domain.Entities;
using LabAi.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabAi.Infrastructure.Persistence.Configurations;

public class SourceDocumentConfiguration : IEntityTypeConfiguration<SourceDocument>
{
    public void Configure(EntityTypeBuilder<SourceDocument> builder)
    {
        builder.ToTable("source_documents");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.SourcePath).HasMaxLength(512).IsRequired();
        builder.Property(d => d.Title).HasMaxLength(256).IsRequired();
        builder.Property(d => d.Version).IsRequired();

        // SHA-256 lowercase hex is always 64 characters; a longer value means the wrong hash was stored.
        builder.Property(d => d.ContentHash).HasMaxLength(64).IsRequired();

        builder.Property(d => d.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(32);

        builder.Property(d => d.IngestedAtUtc)
            .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        // Self-reference for the supersede chain. Restrict, never Cascade: deleting a document must not
        // take its version history with it.
        builder.HasOne<SourceDocument>()
            .WithMany()
            .HasForeignKey(d => d.SupersededByDocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Actor → User: no navigation property on either side, so the FK is declared explicitly.
        // Restrict for the same reason — an account must not be deletable while documents cite it.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(d => d.IngestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Idempotency lookup on ingest, and the Active-only filter the vector store snapshot runs on.
        builder.HasIndex(d => d.ContentHash);
        builder.HasIndex(d => d.Status);
    }
}
