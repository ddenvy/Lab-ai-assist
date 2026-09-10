using LabAi.Domain.Enums;

namespace LabAi.Domain.ValueObjects;

/// <summary>A persisted audit entry returned by the trail for display in the audit journal.</summary>
public sealed record AuditEntry(
    long Id,
    DateTime TimestampUtc,
    long ActorUserId,
    string CorrelationId,
    string QuestionMasked,
    long[] RetrievedChunkIds,
    double TopScore,
    string Model,
    string PromptHash,
    bool AnsweredFromContext,
    RefusalStage RefusalStage,
    string AnswerMasked,
    string? Rationale);
