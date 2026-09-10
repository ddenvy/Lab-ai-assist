using System.Text;
using LabAi.Domain.Abstractions;
using LabAi.Domain.Enums;
using LabAi.Domain.ValueObjects;

namespace LabAi.Infrastructure.Parsing;

/// <summary>
/// Parses Markdown into sections delimited by ATX headings (<c>#</c>..–<c>######</c>).
/// YAML front-matter is stripped, not parsed: title and version for the ingest come from the
/// request, so the block would otherwise become a meaningless first chunk. Heading text inside a
/// fenced code block is content, not structure — the fence state is tracked line by line.
/// </summary>
public sealed class MarkdownDocumentParser : IDocumentParser
{
    public DocumentKind Kind => DocumentKind.Markdown;

    public ParsedDocument Parse(byte[] content)
    {
        var lines = TextDecoder.Decode(content).Split('\n');

        // Normalise away the trailing '\r' of CRLF files; the '\n' split already split lines.
        for (var i = 0; i < lines.Length; i++)
            lines[i] = lines[i].TrimEnd('\r');

        var sections = new List<DocumentSection>();
        var headingStack = new Stack<(int Level, string Title)>();

        var bodyStart = SkipFrontMatter(lines);

        // 1-based bounds of the block's non-blank lines; blank padding around the text is excluded
        // from the citation span because it is excluded from the text itself.
        var firstContentLine = 0;
        var lastContentLine = 0;
        var currentText = new StringBuilder();
        var inFence = false;

        for (var index = bodyStart; index < lines.Length; index++)
        {
            var line = lines[index];
            if (IsFenceDelimiter(line))
                inFence = !inFence;

            if (!inFence && TryParseHeading(line, out var level, out var title))
            {
                FlushSection(sections, headingStack, firstContentLine, lastContentLine, currentText);
                while (headingStack.Count > 0 && headingStack.Peek().Level >= level)
                    headingStack.Pop();
                headingStack.Push((level, title));
                firstContentLine = 0;
                currentText.Clear();
                continue;
            }

            if (line.Trim().Length > 0)
            {
                if (firstContentLine == 0)
                    firstContentLine = index + 1;
                lastContentLine = index + 1;
            }

            currentText.AppendLine(line);
        }

        FlushSection(sections, headingStack, firstContentLine, lastContentLine, currentText);
        return new ParsedDocument(sections);
    }

    /// <summary>
    /// Skips a leading <c>---</c> … <c>---</c> block. Recognised only at the very top of the file,
    /// which is where YAML front-matter may legally appear.
    /// </summary>
    private static int SkipFrontMatter(string[] lines)
    {
        if (lines.Length == 0 || lines[0].TrimEnd() != "---")
            return 0;

        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd() == "---")
                return i + 1;
        }

        return 0; // Unterminated front-matter: treat the whole file as body.
    }

    private static bool IsFenceDelimiter(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("```", StringComparison.Ordinal) ||
               trimmed.StartsWith("~~~", StringComparison.Ordinal);
    }

    private static bool TryParseHeading(string line, out int level, out string title)
    {
        level = 0;
        title = string.Empty;

        var trimmed = line.TrimStart();
        var hashes = 0;
        while (hashes < trimmed.Length && trimmed[hashes] == '#')
            hashes++;

        if (hashes is 0 or > 6 || hashes >= trimmed.Length || trimmed[hashes] != ' ')
            return false;

        level = hashes;
        title = trimmed[(hashes + 1)..].Trim();
        return title.Length > 0;
    }

    private static void FlushSection(
        List<DocumentSection> sections,
        Stack<(int Level, string Title)> headingStack,
        int firstContentLine,
        int lastContentLine,
        StringBuilder currentText)
    {
        if (firstContentLine == 0)
            return;

        var path = string.Join(" > ", headingStack.Reverse().Select(h => h.Title));
        sections.Add(new DocumentSection(
            SectionPath: path,
            Text: currentText.ToString().Trim(),
            PageStart: null,
            PageEnd: null,
            LineStart: firstContentLine,
            LineEnd: lastContentLine));
    }
}
