using System.Text;

namespace LabAi.Infrastructure.Parsing;

/// <summary>
/// Minimal RFC 4180 tokenizer: quoted fields, doubled-quote escapes, commas and newlines inside
/// quotes, CRLF and LF. Blank lines (the artifact of a trailing newline) are skipped; the raw text
/// of a record is an exact slice of the source string, so quoting stays byte-faithful. Instrument
/// exports are machine-generated, so structural corruption — an unterminated quote — fails loudly
/// instead of silently misaligning columns of measurement data.
/// </summary>
internal static class CsvTokenizer
{
    internal sealed record CsvRecord(string[] Fields, string RawText, int StartLine, int EndLine);

    public static List<CsvRecord> Tokenize(string text)
    {
        var records = new List<CsvRecord>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var line = 1;
        var recordStartLine = 1;
        var recordStartIndex = 0;
        var recordEndIndex = 0;
        var hasContent = false;
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var crlf = c == '\r' && i + 1 < text.Length && text[i + 1] == '\n';

            if (inQuotes)
            {
                switch (c)
                {
                    case '"':
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }

                        break;
                    case '\n':
                    case '\r':
                        field.Append('\n');
                        line++;
                        if (crlf)
                            i++;
                        break;
                    default:
                        field.Append(c);
                        break;
                }

                recordEndIndex = i + 1;
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    hasContent = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    hasContent = true;
                    break;
                case '\n':
                case '\r':
                    // The raw slice must not swallow the record terminator: RawText feeds the chunk
                    // text, and embedded \r\n would silently double every line break.
                    if (crlf)
                        i++;
                    FinishRecord(records, fields, field, text, recordStartIndex, i - (crlf ? 1 : 0), recordStartLine, line, hasContent);
                    fields.Clear();
                    field.Clear();
                    hasContent = false;
                    line++;
                    recordStartLine = line;
                    recordStartIndex = i + 1;
                    break;
                default:
                    field.Append(c);
                    hasContent = true;
                    break;
            }

            recordEndIndex = i + 1;
        }

        if (inQuotes)
            throw new InvalidDataException(
                $"CSV line {recordStartLine}: unterminated quoted field; the export file is corrupt.");

        if (hasContent)
            FinishRecord(records, fields, field, text, recordStartIndex, recordEndIndex, recordStartLine, line, true);

        return records;
    }

    private static void FinishRecord(
        List<CsvRecord> records,
        List<string> fields,
        StringBuilder field,
        string text,
        int startIndex,
        int endIndex,
        int startLine,
        int endLine,
        bool hasContent)
    {
        if (!hasContent)
            return;

        fields.Add(field.ToString());
        records.Add(new CsvRecord([.. fields], text[startIndex..endIndex], startLine, endLine));
    }
}
