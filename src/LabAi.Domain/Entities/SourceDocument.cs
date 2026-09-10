using LabAi.Domain.Enums;

namespace LabAi.Domain.Entities;

/// <summary>
/// One ingested source file, at one point in its version history. The file on disk stays the
/// authoritative record; this row is the provenance wrapper that makes every retrieved chunk traceable
/// to a document, a version and the person who loaded it.
/// </summary>
/// <remarks>
/// Mutable on purpose: superseding a document updates <see cref="Status"/> and
/// <see cref="SupersededByDocumentId"/> in the same transaction that inserts the replacement. Rows are
/// never deleted, so the chain of versions survives.
/// </remarks>
public sealed class SourceDocument
{
    public long Id { get; set; }

    /// <summary>Path the content was read from, as supplied at ingest. Provenance, not a live handle.</summary>
    public string SourcePath { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>Document revision, distinct from the row's identity: re-ingesting changed content creates a new row.</summary>
    public int Version { get; set; }

    public DocumentKind Kind { get; set; }

    /// <summary>SHA-256 of the raw file bytes, lowercase hex. Equality on this field is what makes ingest idempotent.</summary>
    public string ContentHash { get; set; } = string.Empty;

    public DocumentStatus Status { get; set; }

    /// <summary>Set when this row is superseded; null while the document is current.</summary>
    public long? SupersededByDocumentId { get; set; }

    public DateTime IngestedAtUtc { get; set; }

    /// <summary>Actor who loaded the document. No navigation property: the journal must stay readable after accounts change.</summary>
    public long IngestedByUserId { get; set; }
}
