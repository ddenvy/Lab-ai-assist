using LabAi.Domain.ValueObjects;

namespace LabAi.Domain.Abstractions;

/// <summary>Append-only audit trail for AI queries. Implemented in Infrastructure with EF interceptor + SQL triggers.</summary>
public interface IAiAuditTrail
{
    Task<long> AppendAsync(AiAuditEntryDraft entry, CancellationToken cancellationToken = default);

    /// <summary>Lists recent audit entries, most recent first.</summary>
    Task<IReadOnlyList<AuditEntry>> ListAsync(int limit = 100, CancellationToken cancellationToken = default);
}
