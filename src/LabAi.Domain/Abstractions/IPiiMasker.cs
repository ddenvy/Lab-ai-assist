namespace LabAi.Domain.Abstractions;

/// <summary>Port for PII masking. Applied to chunk text during ingest, user questions, and prompt composition.</summary>
public interface IPiiMasker
{
    /// <summary>Masks all detected PII in the input text. Returns masked text with [REDACTED] replacements.</summary>
    string Mask(string? text);
}
