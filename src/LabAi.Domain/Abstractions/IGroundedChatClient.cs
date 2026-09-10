namespace LabAi.Domain.Abstractions;

/// <summary>
/// Narrow port around the chat model, used for grounded generation.
/// </summary>
/// <remarks>
/// Deliberately our own interface rather than Semantic Kernel's <c>IChatCompletionService</c>:
/// Domain and Application must stay framework-free (AGENT.md 2.1), and the Google connector is an
/// alpha package that has already renamed its embedding API once. Keeping the surface to one method
/// confines that churn to a single adapter (see docs/adr/ADR-0002).
/// </remarks>
public interface IGroundedChatClient
{
    /// <summary>
    /// Completes one turn against the configured model.
    /// </summary>
    /// <remarks>
    /// A model refusal is ordinary content, not an error — this method only throws for transport,
    /// configuration and quota failures.
    /// </remarks>
    Task<GroundedChatResult> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}
