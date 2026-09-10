using LabAi.Domain.Abstractions;
using LabAi.Domain.Enums;
using LabAi.Domain.ValueObjects;

namespace LabAi.Application.Chunking;

/// <summary>
/// Chunker for instrument CSV exports already grouped by the parser into one section per sample.
/// Every chunk repeats a one-line preamble ("Measurement results, sample X: N data rows.")
/// and the header row — the preamble is what makes a semantic query like "results Sample_003"
/// hit the block without relying on a specific cell value. A sample group larger than the budget
/// is split across row windows; rows are never overlapped (each row is an independent fact, and
/// duplication would yield duplicate citations).
/// </summary>
public sealed class CsvRowGroupChunker : IChunkingStrategy
{
    private const string PreamblePrefix = "Measurement results";
    private const string RowCountSuffix = "data rows";

    private readonly int maxChunkChars;

    public CsvRowGroupChunker(int maxChunkChars = 2000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChunkChars, 1);
        this.maxChunkChars = maxChunkChars;
    }

    public IEnumerable<DocumentKind> SupportedKinds => [DocumentKind.Csv];

    public IReadOnlyList<ChunkDraft> Chunk(ParsedDocument document)
    {
        var chunks = new List<ChunkDraft>();

        foreach (var section in document.Sections)
        {
            var lines = section.Text.Split('\n');
            if (lines.Length == 0)
                continue;

            var header = lines[0];
            var rows = lines[1..];
            if (rows.Length == 0)
                continue; // A header line alone carries no data rows to retrieve.
            var preamble = BuildPreamble(section.SectionPath, rows.Length);

            // Budget for raw rows once the preamble, the repeated header and the separators
            // are accounted for.
            var rowBudget = maxChunkChars - preamble.Length - 1 - header.Length - 1;
            if (rowBudget < 1)
            {
                // Degenerate config (header alone nearly fills the budget): emit each row as
                // its own chunk even if it overflows, rather than silently dropping rows.
                for (var i = 0; i < rows.Length; i++)
                    AddChunk(chunks, section, preamble, header, [rows[i]], i);
                continue;
            }

            var window = new List<string>();
            var windowLength = 0;
            var windowStart = 0;
            for (var i = 0; i < rows.Length; i++)
            {
                var candidate = window.Count == 0 ? rows[i].Length : windowLength + 1 + rows[i].Length;
                if (candidate > rowBudget && window.Count > 0)
                {
                    AddChunk(chunks, section, preamble, header, window, windowStart);
                    window.Clear();
                    windowLength = 0;
                    windowStart = i;
                }

                window.Add(rows[i]);
                windowLength = window.Count == 1 ? rows[i].Length : windowLength + 1 + rows[i].Length;
            }

            AddChunk(chunks, section, preamble, header, window, windowStart);
        }

        return chunks;
    }

    private static string BuildPreamble(string sectionPath, int rowCount)
    {
        return sectionPath == DocumentSection.UngroupedRowsPath
            ? $"{PreamblePrefix}: {rowCount} {RowCountSuffix}."
            : $"{PreamblePrefix}, sample {sectionPath}: {rowCount} {RowCountSuffix}.";
    }

    private void AddChunk(
        List<ChunkDraft> chunks,
        DocumentSection section,
        string preamble,
        string header,
        List<string> rows,
        int firstRowIndex)
    {
        var text = $"{preamble}\n{header}\n{string.Join('\n', rows)}";
        chunks.Add(new ChunkDraft(
            SectionPath: section.SectionPath,
            Text: text,
            PageStart: section.PageStart,
            PageEnd: section.PageEnd,
            // The parser joins each row as exactly one line, so a window's span is derived
            // arithmetically from the section's first data line. This assumes contiguous rows —
            // true for instrument exports; the parser skips blank rows, which would shift the
            // mapping and is not modelled here.
            LineStart: section.LineStart + firstRowIndex,
            LineEnd: section.LineStart + firstRowIndex + rows.Count - 1));
    }
}
