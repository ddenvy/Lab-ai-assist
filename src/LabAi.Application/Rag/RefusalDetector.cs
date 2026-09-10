using LabAi.Domain.Enums;

namespace LabAi.Application.Rag;

/// <summary>
/// Detects whether the model refused to answer (threshold gate before LLM call, or
/// post-generation phrase detection). The refusal phrase is normalised (lowercase, stripped
/// of punctuation and extra whitespace) so that minor variations like "Not found in sources."
/// vs "not found in sources" are caught reliably.
/// </summary>
public static class RefusalDetector
{
    private const string RefusalPhrase = "not found in sources";

    /// <summary>
    /// Checks if the answer text begins with the canonical refusal phrase after normalisation.
    /// Returns true when the model explicitly states it cannot answer from the provided context.
    /// </summary>
    public static bool IsRefusal(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return true; // Empty answer counts as refusal.

        var normalised = Normalise(answer);
        return normalised.StartsWith(RefusalPhrase);
    }

    /// <summary>
    /// Determines the refusal stage. ThresholdGate means zero hits met MinScore so the LLM was
    /// never called. ModelRefusal means the LLM was called but returned the refusal phrase.
    /// None means a substantive answer was produced.
    /// </summary>
    public static RefusalStage DetermineStage(bool hadRetrievedChunks, string? answer)
    {
        if (!hadRetrievedChunks)
            return RefusalStage.ThresholdGate;

        return IsRefusal(answer) ? RefusalStage.ModelRefusal : RefusalStage.None;
    }

    /// <summary>
    /// Extracts the rationale section from the model's answer. Everything after "--- Rationale:"
    /// (case-insensitive) is returned, trimmed. If no rationale marker is found, returns null.
    /// </summary>
    public static string? ExtractRationale(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return null;

        const string marker = "--- rationale:";
        var idx = answer.AsSpan().IndexOf(marker.AsSpan(), StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        return answer[(idx + marker.Length)..].Trim();
    }

    private static string Normalise(string text)
    {
        // Lowercase, strip punctuation except spaces, collapse whitespace.
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
                sb.Append(char.ToLowerInvariant(c));
        }

        var result = sb.ToString();
        return System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ").Trim();
    }
}
