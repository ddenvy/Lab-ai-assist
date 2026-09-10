namespace LabAi.Domain.ValueObjects;

/// <summary>
/// One structurally meaningful block of a parsed document: a markdown section under its heading
/// stack, one instrument sample's rows, a JSON element, or a PDF page.
/// </summary>
/// <param name="SectionPath">
/// Human-readable location inside the document, e.g. <c>"4. Система > 4.2 Критерии"</c>,
/// <c>"Sample_003"</c> or <c>"Page 2"</c>. Becomes part of the chunk's citation context, so it must
/// stay stable across re-ingests of unchanged content.
/// </param>
/// <param name="Text">Raw text of the block. PII masking happens later, in the ingest pipeline.</param>
/// <param name="PageStart">1-based first page, for formats with pages (PDF); null otherwise.</param>
/// <param name="PageEnd">1-based last page; normally equal to <paramref name="PageStart"/>.</param>
/// <param name="LineStart">1-based first line inside the source file, for line-oriented formats.</param>
/// <param name="LineEnd">1-based last line inside the source file.</param>
public sealed record DocumentSection(
    string SectionPath,
    string Text,
    int? PageStart = null,
    int? PageEnd = null,
    int? LineStart = null,
    int? LineEnd = null)
{
    /// <summary>
    /// Section path for a CSV without a <c>Sample Name</c> column, where all rows land in one
    /// block. Shared by the CSV parser (producer) and the CSV chunker (consumer), which must
    /// agree on the sentinel instead of duplicating a magic string.
    /// </summary>
    public const string UngroupedRowsPath = "Rows";
}
