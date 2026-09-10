namespace LabAi.Domain.Enums;

/// <summary>Persisted outcome of an ingest command (enum as TEXT — forensic-readable).</summary>
public enum IngestOutcome
{
    /// <summary>A new document version was stored, together with its chunks and vectors.</summary>
    Created,

    /// <summary>
    /// Identical content is already active. Nothing was parsed, nothing was embedded — the
    /// embedding model is never billed for an idempotent re-ingest.
    /// </summary>
    Duplicate,
}
