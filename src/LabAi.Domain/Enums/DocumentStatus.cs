namespace LabAi.Domain.Enums;

/// <summary>
/// Lifecycle of an ingested document. Re-ingesting a changed file never deletes the old row: it marks
/// it <see cref="Superseded"/> and points at its replacement, so the corpus retains the version history
/// a regulated record requires. Only <see cref="Active"/> documents are searchable.
/// </summary>
public enum DocumentStatus
{
    Active,
    Superseded
}
