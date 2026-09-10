namespace LabAi.Domain.Entities;

/// <summary>
/// One retrievable fragment of a document, stored with the coordinates needed to cite it and the
/// vector needed to find it. Immutable: a chunk is never edited, only replaced by re-ingesting its
/// document, so a citation in an old audit row keeps pointing at exactly the text that was used.
/// </summary>
public sealed class Chunk
{
    public long Id { get; init; }

    public long DocumentId { get; init; }

    /// <summary>Navigation property to the parent document for citation metadata.</summary>
    public SourceDocument? Document { get; init; }

    /// <summary>Position within the document, zero-based. Together with <see cref="DocumentId"/> this is unique.</summary>
    public int ChunkIndex { get; init; }

    /// <summary>
    /// The fragment as embedded and as shown in a citation — already PII-masked. Masking happens before
    /// the text is ever handed to the embedding model, so the unmasked original never crosses the trust
    /// boundary. The file on disk remains the authoritative copy.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Breadcrumb of headings or column groups, e.g. <c>h1 &gt; h2 &gt; h3</c>. Shown in the sources panel.</summary>
    public string SectionPath { get; init; } = string.Empty;

    /// <summary>Page span for paginated formats (PDF); null for formats with no pages.</summary>
    public int? PageStart { get; init; }
    public int? PageEnd { get; init; }

    /// <summary>Line span for line-oriented formats (CSV, Markdown); null when the fragment has no line identity.</summary>
    public int? LineStart { get; init; }
    public int? LineEnd { get; init; }

    /// <summary>SHA-256 of <see cref="Text"/>, lowercase hex. Detects accidental drift between stored text and stored vector.</summary>
    public string ContentHash { get; init; } = string.Empty;

    /// <summary>
    /// Model that produced <see cref="Embedding"/>. Persisted per chunk, not per database: vectors from
    /// different models are not comparable, and the store refuses to search a mixed collection.
    /// </summary>
    public string EmbeddingModelId { get; init; } = string.Empty;

    /// <summary>Vector length in floats. Kept out of the blob so the guard can check it without decoding.</summary>
    public int EmbeddingDimension { get; init; }

    /// <summary>
    /// Raw IEEE-754 float32 little-endian, no header, exactly <c>EmbeddingDimension * 4</c> bytes,
    /// L2-normalised at write time. A header would only complicate the zero-copy
    /// <c>MemoryMarshal.Cast</c> that makes brute-force search cheap.
    /// </summary>
    public byte[] Embedding { get; init; } = [];

    public DateTime CreatedAtUtc { get; init; }
}
