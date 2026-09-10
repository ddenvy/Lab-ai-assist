namespace LabAi.Domain.ValueObjects;

/// <summary>Detailed citation information for display in the sources panel.</summary>
public sealed record SourceCitationDetail(
    int Index,
    long ChunkId,
    long DocumentId,
    string DocumentTitle,
    int Version,
    string SectionPath,
    string Text,
    double Score);
