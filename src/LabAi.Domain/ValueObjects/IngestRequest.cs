namespace LabAi.Domain.ValueObjects;

/// <summary>
/// One ingest command: the raw file plus the provenance the journal needs. <paramref name="Content"/>
/// holds the exact file bytes — the SHA-256 that makes ingest idempotent is computed over them, so
/// what was hashed is provably what was on disk.
/// </summary>
/// <param name="SourcePath">Path the content was read from. Provenance, and the key a changed file supersedes its predecessor by.</param>
/// <param name="Title">Human title shown in citations; also part of the embedding-time context header.</param>
/// <param name="Version">Document revision supplied by the caller; re-ingesting changed content creates a new row, not an update.</param>
/// <param name="Kind">Selects the parser and the chunking strategy.</param>
/// <param name="Content">Exact file bytes.</param>
/// <param name="IngestedByUserId">Actor who performed the ingest; stored on the document row.</param>
public sealed record IngestRequest(
    string SourcePath,
    string Title,
    int Version,
    Enums.DocumentKind Kind,
    byte[] Content,
    long IngestedByUserId);
