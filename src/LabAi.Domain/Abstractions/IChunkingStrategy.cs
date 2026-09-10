namespace LabAi.Domain.Abstractions;

/// <summary>
/// Turns a parsed document into retrieval-ready chunks under a character budget. Mirrors
/// <see cref="IDocumentParser"/>: strategies are selected by document kind. Pure text logic —
/// BCL only, no framework types.
/// </summary>
public interface IChunkingStrategy
{
    /// <summary>
    /// Document kinds this strategy can chunk. Unlike parsers, a strategy may legitimately
    /// serve several kinds (the generic line windower covers JSON and PDF pages alike);
    /// registrations must not overlap and must cover every kind.
    /// </summary>
    IEnumerable<Enums.DocumentKind> SupportedKinds { get; }

    IReadOnlyList<ValueObjects.ChunkDraft> Chunk(ValueObjects.ParsedDocument document);
}
