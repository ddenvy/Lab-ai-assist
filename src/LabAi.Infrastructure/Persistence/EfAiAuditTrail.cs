using System.Security.Cryptography;
using System.Text;
using LabAi.Domain.Abstractions;
using LabAi.Domain.Entities;
using LabAi.Domain.Enums;
using LabAi.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace LabAi.Infrastructure.Persistence;

/// <summary>
/// EF-backed append-only audit trail. Every append reads the current tail hash, hashes the new row
/// over its fields plus that tail hash, and inserts — so the journal is a hash chain: editing or
/// deleting any row breaks every hash after it. SQL triggers created in the migration abort UPDATE
/// and DELETE at the database level, so append-only holds even for a caller with direct DB access.
/// </summary>
public sealed class EfAiAuditTrail(IDbContextFactory<LabAiDbContext> contextFactory) : IAiAuditTrail
{
    /// <summary>Tail hash of an empty journal: 64 zeros, the SHA-256 hex width.</summary>
    private const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    public async Task<long> AppendAsync(AiAuditEntryDraft entry, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var tail = await db.AiAuditEntries
            .OrderByDescending(e => e.Id)
            .Select(e => new { e.Id, e.RowHash })
            .FirstOrDefaultAsync(cancellationToken);

        var chunkIds = string.Join(",", entry.RetrievedChunkIds);
        var prevHash = tail?.RowHash ?? GenesisHash;

        var row = new AiAuditEntry
        {
            TimestampUtc = entry.TimestampUtc,
            ActorUserId = entry.ActorUserId,
            CorrelationId = entry.CorrelationId,
            QuestionMasked = entry.QuestionMasked,
            RetrievedChunkIds = chunkIds,
            TopScore = entry.TopScore,
            Model = entry.Model,
            PromptHash = entry.PromptHash,
            AnsweredFromContext = entry.AnsweredFromContext,
            RefusalStage = entry.RefusalStage,
            AnswerMasked = entry.AnswerMasked,
            Rationale = entry.Rationale,
            PrevHash = prevHash,
            RowHash = ComputeRowHash(entry, chunkIds, prevHash)
        };

        db.AiAuditEntries.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return row.Id;
    }

    public async Task<IReadOnlyList<AuditEntry>> ListAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var rows = await db.AiAuditEntries
            .OrderByDescending(e => e.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows.Select(e => new AuditEntry(
            e.Id,
            e.TimestampUtc,
            e.ActorUserId,
            e.CorrelationId,
            e.QuestionMasked,
            ParseChunkIds(e.RetrievedChunkIds),
            e.TopScore,
            e.Model,
            e.PromptHash,
            e.AnsweredFromContext,
            e.RefusalStage,
            e.AnswerMasked,
            e.Rationale)).ToList();
    }

    private static long[] ParseChunkIds(string stored) =>
        stored.Length == 0
            ? []
            : stored.Split(',').Select(long.Parse).ToArray();

    /// <summary>
    /// Length-prefixed field encoding, same discipline as the prompt hash: without lengths, a field
    /// containing the separator could shift bytes between fields and forge a colliding row hash.
    /// </summary>
    internal static string ComputeRowHash(AiAuditEntryDraft entry, string chunkIds, string prevHash)
    {
        var builder = new StringBuilder();
        AppendField(builder, entry.TimestampUtc.ToString("O"));
        AppendField(builder, entry.ActorUserId.ToString());
        AppendField(builder, entry.CorrelationId);
        AppendField(builder, entry.QuestionMasked);
        AppendField(builder, chunkIds);
        AppendField(builder, entry.TopScore.ToString("R"));
        AppendField(builder, entry.Model);
        AppendField(builder, entry.PromptHash);
        AppendField(builder, entry.AnsweredFromContext ? "1" : "0");
        AppendField(builder, entry.RefusalStage.ToString());
        AppendField(builder, entry.AnswerMasked);
        AppendField(builder, entry.Rationale ?? string.Empty);
        AppendField(builder, prevHash);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void AppendField(StringBuilder builder, string value) =>
        builder.Append(value.Length).Append(':').Append(value).Append('|');
}
