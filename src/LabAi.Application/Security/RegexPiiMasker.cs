using System.Text.RegularExpressions;
using LabAi.Domain.Abstractions;

namespace LabAi.Application.Security;

/// <summary>
/// Masks personally identifiable information (PII) using regex patterns. Applied at three points:
/// 1. During document ingest (chunk text before embedding)
/// 2. On user questions before RAG pipeline processing
/// 3. Re-masked in PromptComposer to ensure no PII leaks into the prompt hash or audit log
/// </summary>
public sealed class RegexPiiMasker : IPiiMasker
{
    private static readonly Regex EmailPattern = new(
        @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PhonePattern = new(
        @"\+?[\d\s\-()]{7,15}",
        RegexOptions.Compiled);

    private static readonly Regex SsnPattern = new(
        @"\b\d{3}-\d{2}-\d{4}\b",
        RegexOptions.Compiled);

    private const string MaskedValue = "[REDACTED]";

    /// <summary>
    /// Masks all detected PII patterns in the input text. Returns the masked text with [REDACTED]
    /// replacing each match. Safe to call on null/empty strings.
    /// </summary>
    public string Mask(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text ?? string.Empty;

        var result = text;
        result = EmailPattern.Replace(result, MaskedValue);
        result = PhonePattern.Replace(result, MaskedValue);
        result = SsnPattern.Replace(result, MaskedValue);

        return result;
    }
}
