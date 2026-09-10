using LabAi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabAi.Infrastructure.Persistence.Configurations;

public class AiAuditEntryConfiguration : IEntityTypeConfiguration<AiAuditEntry>
{
    public void Configure(EntityTypeBuilder<AiAuditEntry> builder)
    {
        builder.ToTable("ai_audit_entries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.CorrelationId).HasMaxLength(64).IsRequired();
        builder.Property(e => e.PromptHash).HasMaxLength(64).IsRequired();
        builder.Property(e => e.PrevHash).HasMaxLength(64).IsRequired();
        builder.Property(e => e.RowHash).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Model).HasMaxLength(128).IsRequired();

        // Question and answer are unbounded TEXT on purpose: a refusal rationale or a long grounded
        // answer must never fail the append, because the journal is written before the answer returns.
        builder.Property(e => e.QuestionMasked).IsRequired();
        builder.Property(e => e.AnswerMasked).IsRequired();
        builder.Property(e => e.RetrievedChunkIds).IsRequired();

        builder.Property(e => e.RefusalStage).HasConversion<string>().HasMaxLength(32);

        builder.Property(e => e.TimestampUtc)
            .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        // Journal reads are "most recent first, page by page"; the covering index keeps that cheap
        // as the table grows for the lifetime of the deployment.
        builder.HasIndex(e => e.TimestampUtc);
        builder.HasIndex(e => e.CorrelationId);
    }
}
