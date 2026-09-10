namespace LabAi.Domain.ValueObjects;

/// <summary>Outcome of one ingest command, as reported back to the caller and the UI.</summary>
/// <param name="DocumentId">New document row for <see cref="Enums.IngestOutcome.Created"/>; the already-active row for <see cref="Enums.IngestOutcome.Duplicate"/>.</param>
/// <param name="Outcome">Created or Duplicate.</param>
/// <param name="ChunksCreated">Number of chunks stored; zero for duplicates and empty documents.</param>
/// <param name="SupersededDocumentId">Previous active version replaced by this ingest; null when there was none.</param>
public sealed record IngestResult(
    long DocumentId,
    Enums.IngestOutcome Outcome,
    int ChunksCreated,
    long? SupersededDocumentId);
