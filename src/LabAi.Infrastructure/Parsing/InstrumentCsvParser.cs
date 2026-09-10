using System.Text;
using LabAi.Domain.Abstractions;
using LabAi.Domain.Enums;
using LabAi.Domain.ValueObjects;

namespace LabAi.Infrastructure.Parsing;

/// <summary>
/// Parses instrument CSV exports in the shape produced by the chromatography data system
/// (one row per peak: <c>Sample Name, Method Name, Status, Created At, Peak #, Retention Time (s),
/// Height, Area, FWHM, Plates, Tailing</c>). Rows are grouped into one section per sample, because
/// a peak row alone is not semantically retrievable — "results Sample_003" must match a block
/// that contains the whole sample. The UTF-8 BOM that the export writes is removed; an empty
/// <c>Tailing</c> cell is legal. A CSV without a <c>Sample Name</c> column falls back to one
/// section covering all rows.
/// </summary>
public sealed class InstrumentCsvParser : IDocumentParser
{
    private const string GroupColumn = "Sample Name";

    public DocumentKind Kind => DocumentKind.Csv;

    public ParsedDocument Parse(byte[] content)
    {
        var records = CsvTokenizer.Tokenize(TextDecoder.Decode(content));
        if (records.Count == 0)
            return ParsedDocument.Empty;

        var header = records[0];
        var groupIndex = Array.FindIndex(header.Fields, f => f.Trim() == GroupColumn);

        foreach (var record in records.Skip(1))
        {
            if (record.Fields.Length != header.Fields.Length)
            {
                throw new InvalidDataException(
                    $"CSV line {record.StartLine}: expected {header.Fields.Length} columns, " +
                    $"found {record.Fields.Length}. Misaligned instrument data must not be ingested.");
            }
        }

        if (groupIndex < 0)
        {
            return new ParsedDocument(
            [
                new DocumentSection(
                    SectionPath: DocumentSection.UngroupedRowsPath,
                    Text: JoinRecords(records),
                    LineStart: records[0].StartLine,
                    LineEnd: records[^1].EndLine)
            ]);
        }

        var sections = new List<DocumentSection>();
        var groups = new Dictionary<string, List<CsvTokenizer.CsvRecord>>(StringComparer.Ordinal);
        foreach (var record in records.Skip(1))
        {
            var key = record.Fields.Length > groupIndex && record.Fields[groupIndex].Trim().Length > 0
                ? record.Fields[groupIndex].Trim()
                : "(no sample name)";
            if (!groups.TryGetValue(key, out var rows))
                groups[key] = rows = [];
            rows.Add(record);
        }

        foreach (var (sample, rows) in groups)
        {
            sections.Add(new DocumentSection(
                SectionPath: sample,
                Text: header.RawText + "\n" + JoinRecords(rows),
                LineStart: rows[0].StartLine,
                LineEnd: rows[^1].EndLine));
        }

        return new ParsedDocument(sections);
    }

    private static string JoinRecords(List<CsvTokenizer.CsvRecord> records)
    {
        var builder = new StringBuilder();
        foreach (var record in records)
        {
            if (builder.Length > 0)
                builder.Append('\n');
            builder.Append(record.RawText);
        }

        return builder.ToString();
    }
}
