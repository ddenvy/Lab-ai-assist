namespace LabAi.Domain.Abstractions;

/// <summary>
/// Turns raw document bytes into sections. One implementation per <see cref="Enums.DocumentKind"/>;
/// the ingest pipeline selects by kind, which it derives from the file extension. Parsing is pure
/// CPU work on content already in memory, so the contract is synchronous.
/// </summary>
public interface IDocumentParser
{
    /// <summary>Kind this parser understands; unique across registered parsers.</summary>
    Enums.DocumentKind Kind { get; }

    /// <exception cref="InvalidDataException">Content is not valid for this kind.</exception>
    ValueObjects.ParsedDocument Parse(byte[] content);
}
