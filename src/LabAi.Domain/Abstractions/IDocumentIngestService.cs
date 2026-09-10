namespace LabAi.Domain.Abstractions;

/// <summary>Application-side ingest entry point; the HTTP endpoint translates requests into this.</summary>
public interface IDocumentIngestService
{
    /// <summary>
    /// Ingests one file: hash → parse → chunk → embed → persist. Idempotent by content hash.
    /// </summary>
    /// <exception cref="InvalidDataException">Content cannot be parsed for the requested document kind.</exception>
    /// <exception cref="InvalidOperationException">No parser/strategy for the kind, or the embedding model returned inconsistent vectors.</exception>
    Task<ValueObjects.IngestResult> IngestAsync(ValueObjects.IngestRequest request, CancellationToken cancellationToken = default);
}
