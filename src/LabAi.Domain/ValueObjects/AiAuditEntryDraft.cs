using LabAi.Domain.Enums;

namespace LabAi.Domain.ValueObjects;

/// <summary>Draft of an AI audit entry to be appended. The actual Id and PrevHash are assigned by the trail.</summary>
public sealed record AiAuditEntryDraft(
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

/// <summary>A chunk cited in a grounded answer, with its positional index [S1]..[Sn].</summary>
public sealed record CitedChunk(int Index, long ChunkId, long DocumentId, double Score);

/// <summary>The answer returned by the RAG pipeline to the caller.</summary>
public sealed record RagAnswer(
    string Answer,
    bool AnsweredFromContext,
    RefusalStage RefusalStage,
    IReadOnlyList<CitedChunk> Citations,
    string? Rationale,
    long AuditEntryId,
    string CorrelationId);
