namespace LabAi.Domain.Enums;

/// <summary>
/// Ingest format, which selects the parser. Persisted as TEXT so a database opened by hand still
/// explains itself (21 CFR Part 11 record legibility).
/// </summary>
public enum DocumentKind
{
    Markdown,
    Pdf,
    Csv,
    Json
}
