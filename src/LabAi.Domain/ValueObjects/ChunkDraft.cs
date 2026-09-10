namespace LabAi.Domain.ValueObjects;

/// <summary>
/// One retrieval unit produced by a chunking strategy: the text that will be masked, embedded
/// and stored, plus the source coordinates needed for citations. Chunk indices are assigned by
/// the ingest pipeline, not by the chunker.
/// </summary>
/// <param name="SectionPath">Location inside the source document, inherited from the section the chunk was built from. Merged chunks carry the common parent path.</param>
/// <param name="Text">Chunk text before PII masking, which happens later in the ingest pipeline.</param>
/// <param name="PageStart">1-based first page, for formats with pages (PDF); null otherwise.</param>
/// <param name="PageEnd">1-based last page; normally equal to <paramref name="PageStart"/>.</param>
/// <param name="LineStart">1-based first line inside the source file, for line-oriented formats.</param>
/// <param name="LineEnd">1-based last line inside the source file.</param>
public sealed record ChunkDraft(
    string SectionPath,
    string Text,
    int? PageStart = null,
    int? PageEnd = null,
    int? LineStart = null,
    int? LineEnd = null);
