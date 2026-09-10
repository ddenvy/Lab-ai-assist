namespace LabAi.Domain.ValueObjects;

/// <summary>
/// Result of turning raw document bytes into <see cref="DocumentSection"/> blocks. Parsing is
/// format-specific (Infrastructure); everything downstream — chunking, masking, embedding — sees
/// only this shape.
/// </summary>
public sealed record ParsedDocument(IReadOnlyList<DocumentSection> Sections)
{
    public static readonly ParsedDocument Empty = new([]);
}
