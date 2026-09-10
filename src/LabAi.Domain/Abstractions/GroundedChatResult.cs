namespace LabAi.Domain.Abstractions;

/// <summary>
/// Raw output of one grounded chat turn.
/// </summary>
/// <param name="Text">Model output, verbatim. Refusal detection and citation mapping are the caller's job.</param>
/// <param name="ModelId">
/// The model that served the call. Carried explicitly so the audit entry can record it: the upstream
/// response does not expose a trustworthy model id, so the configured value is authoritative.
/// </param>
public sealed record GroundedChatResult(string Text, string ModelId);
