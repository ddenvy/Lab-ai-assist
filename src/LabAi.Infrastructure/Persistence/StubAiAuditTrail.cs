using LabAi.Domain.Abstractions;
using LabAi.Domain.Enums;
using LabAi.Domain.ValueObjects;

namespace LabAi.Infrastructure.Persistence;

/// <summary>
/// Stub implementation of the AI audit trail for Milestone 4. Returns a synthetic ID without
/// persisting anything. The real EF-backed append-only implementation with interceptor + SQL
/// triggers arrives in Milestone 5. Until then, the audit entry ID in responses is not durable.
/// </summary>
public sealed class StubAiAuditTrail : IAiAuditTrail
{
    private long _nextId = 1;
    private readonly List<AuditEntry> _entries = new();

    public Task<long> AppendAsync(AiAuditEntryDraft entry, CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _nextId) - 1;

        // Store a stub entry for display purposes.
        lock (_entries)
        {
            _entries.Add(new AuditEntry(
                Id: id,
                TimestampUtc: entry.TimestampUtc,
                ActorUserId: entry.ActorUserId,
                CorrelationId: entry.CorrelationId,
                QuestionMasked: entry.QuestionMasked,
                RetrievedChunkIds: entry.RetrievedChunkIds,
                TopScore: entry.TopScore,
                Model: entry.Model,
                PromptHash: entry.PromptHash,
                AnsweredFromContext: entry.AnsweredFromContext,
                RefusalStage: entry.RefusalStage,
                AnswerMasked: entry.AnswerMasked,
                Rationale: entry.Rationale));
        }

        return Task.FromResult(id);
    }

    public Task<IReadOnlyList<AuditEntry>> ListAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        lock (_entries)
        {
            var result = _entries.OrderByDescending(e => e.Id).Take(limit).ToList();
            return Task.FromResult((IReadOnlyList<AuditEntry>)result);
        }
    }
}
