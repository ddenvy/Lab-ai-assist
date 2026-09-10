namespace LabAi.Domain.Enums;

/// <summary>
/// Stage at which the RAG pipeline refused to provide a grounded answer.
/// ThresholdGate means zero chunks met MinScore so the LLM was never called.
/// ModelRefusal means the LLM was called but returned the refusal phrase.
/// UpstreamFailure means the LLM call itself failed (e.g. a transient HTTP 503 that survived
/// retries); the query is still journaled so no request ever vanishes from the audit trail.
/// None means a substantive answer was produced from retrieved context.
/// </summary>
public enum RefusalStage
{
    None,
    ThresholdGate,
    ModelRefusal,
    UpstreamFailure
}
