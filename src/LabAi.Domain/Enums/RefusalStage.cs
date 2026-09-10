namespace LabAi.Domain.Enums;

/// <summary>
/// Stage at which the RAG pipeline refused to provide a grounded answer.
/// ThresholdGate means zero chunks met MinScore so the LLM was never called.
/// ModelRefusal means the LLM was called but returned the refusal phrase.
/// None means a substantive answer was produced from retrieved context.
/// </summary>
public enum RefusalStage
{
    None,
    ThresholdGate,
    ModelRefusal
}
