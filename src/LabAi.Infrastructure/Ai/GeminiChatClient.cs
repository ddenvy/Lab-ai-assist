using LabAi.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Google;

namespace LabAi.Infrastructure.Ai;

/// <summary>
/// Adapter over the Semantic Kernel Google (Gemini) chat connector. One of exactly two files in the
/// solution permitted to name a Semantic Kernel type — see docs/adr/ADR-0002 for why the blast radius
/// of the alpha connector is kept that small.
/// </summary>
/// <param name="options">Resolved Gemini settings.</param>
/// <param name="resolveService">
/// Lazily resolves the connector's chat service. Lazy because registering it with an empty key
/// succeeds while resolving it throws; deferring the call lets the adapter fail with
/// <see cref="GeminiNotConfiguredException"/> instead.
/// </param>
public sealed class GeminiChatClient(
    GeminiOptions options,
    Func<IChatCompletionService> resolveService,
    ILogger<GeminiChatClient> logger) : IGroundedChatClient
{
    public async Task<GroundedChatResult> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        if (!options.IsConfigured)
            throw new GeminiNotConfiguredException();

        var history = new ChatHistory();
        history.AddSystemMessage(systemPrompt);
        history.AddUserMessage(userPrompt);

        var contents = await resolveService()
            .GetChatMessageContentsAsync(history, ExecutionSettings(), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (contents.Count == 0)
        {
            // A safety-blocked or truncated turn comes back with no candidates. Surfaced as a warning
            // rather than swallowed: an empty answer that reaches the audit log must be explainable.
            logger.LogWarning("Gemini returned no candidates for {ModelId}", options.ChatModelId);
        }

        var text = contents.Count > 0 ? contents[0].Content ?? string.Empty : string.Empty;

        // AGENT.md 3.2: sizes and identifiers only — never the prompt or the answer.
        logger.LogInformation(
            "Gemini chat completed on {ModelId}: prompt {PromptChars} chars, candidates {CandidateCount}, answer {AnswerChars} chars",
            options.ChatModelId,
            systemPrompt.Length + userPrompt.Length,
            contents.Count,
            text.Length);

        return new GroundedChatResult(text, options.ChatModelId);
    }

    private static GeminiPromptExecutionSettings ExecutionSettings() => new()
    {
        // Temperature 0 on purpose. Reproducibility is what turns PromptHash into evidence: the same
        // hash against the same model is meant to imply the same answer.
        Temperature = 0.0,
        TopP = 0.95,
        MaxTokens = 2048,

        // Gemini requires temperature 1.0 whenever candidateCount is above 1. We want temperature 0,
        // so the candidate count is pinned rather than left to the connector default.
        CandidateCount = 1,
    };
}
