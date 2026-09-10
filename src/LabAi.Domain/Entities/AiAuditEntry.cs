using LabAi.Domain.Enums;

namespace LabAi.Domain.Entities;

/// <summary>
/// One append-only row of the AI audit journal: every question answered by the RAG pipeline is
/// recorded here before the answer is returned to the caller, so the journal is the authoritative
/// record of what the assistant said and on which evidence.
/// </summary>
/// <remarks>
/// Append-only by construction: SQL triggers in the migration abort every UPDATE and DELETE on the
/// table, and <see cref="RowHash"/> chains each row to <see cref="PrevHash"/>, so removing or editing
/// a row breaks the chain for every row after it. Question and answer text is stored PII-masked.
/// </remarks>
public sealed class AiAuditEntry
{
    public long Id { get; init; }

    public DateTime TimestampUtc { get; init; }

    /// <summary>Actor who asked the question. No navigation property: the journal must outlive account changes.</summary>
    public long ActorUserId { get; init; }

    public string CorrelationId { get; init; } = string.Empty;

    public string QuestionMasked { get; init; } = string.Empty;

    /// <summary>Comma-separated chunk ids retrieved for the question; empty string when nothing was retrieved.</summary>
    public string RetrievedChunkIds { get; init; } = string.Empty;

    public double TopScore { get; init; }

    public string Model { get; init; } = string.Empty;

    /// <summary>SHA-256 of the composed prompt, lowercase hex. Ties a journal row to exactly what the model saw.</summary>
    public string PromptHash { get; init; } = string.Empty;

    public bool AnsweredFromContext { get; init; }

    public RefusalStage RefusalStage { get; init; }

    public string AnswerMasked { get; init; } = string.Empty;

    public string? Rationale { get; init; }

    /// <summary><see cref="RowHash"/> of the previous row; 64 zeros for the genesis row.</summary>
    public string PrevHash { get; init; } = string.Empty;

    /// <summary>SHA-256 over the row's fields plus <see cref="PrevHash"/>, lowercase hex.</summary>
    public string RowHash { get; init; } = string.Empty;
}
