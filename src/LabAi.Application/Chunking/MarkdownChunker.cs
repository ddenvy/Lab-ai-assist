using LabAi.Domain.Abstractions;
using LabAi.Domain.Enums;
using LabAi.Domain.ValueObjects;

namespace LabAi.Application.Chunking;

/// <summary>
/// Section-aware chunker for markdown documents. Adjacent sibling sections (same parent path)
/// are merged while the combined text fits the budget — a three-line section on its own is not
/// retrievable. A section larger than the budget is split on sentence boundaries with a rolling
/// overlap, so a criterion cut at the boundary appears complete in the next window. Merged
/// chunks carry the common parent path; split chunks keep their section's coordinates.
/// </summary>
public sealed class MarkdownChunker : IChunkingStrategy
{
    private readonly int maxChunkChars;
    private readonly int overlapChars;

    public MarkdownChunker(int maxChunkChars = 2000, int overlapChars = 200)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChunkChars, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(overlapChars, maxChunkChars);

        this.maxChunkChars = maxChunkChars;
        this.overlapChars = overlapChars;
    }

    public IEnumerable<DocumentKind> SupportedKinds => [DocumentKind.Markdown];

    public IReadOnlyList<ChunkDraft> Chunk(ParsedDocument document)
    {
        var chunks = new List<ChunkDraft>();

        var run = new List<DocumentSection>();
        foreach (var section in document.Sections)
        {
            if (string.IsNullOrWhiteSpace(section.Text))
                continue;

            var runParent = run.Count > 0 ? ParentOf(run[0].SectionPath) : null;
            if (run.Count > 0 && runParent != ParentOf(section.SectionPath))
            {
                EmitRun(run, chunks);
                run.Clear();
            }

            run.Add(section);
        }

        EmitRun(run, chunks);
        return chunks;
    }

    private void EmitRun(List<DocumentSection> run, List<ChunkDraft> chunks)
    {
        if (run.Count == 0)
            return;

        var runParent = ParentOf(run[0].SectionPath);
        var group = new List<DocumentSection>();
        var groupLength = 0;

        foreach (var section in run)
        {
            if (section.Text.Length > maxChunkChars)
            {
                EmitGroup(group, runParent, chunks);
                group.Clear();
                groupLength = 0;
                EmitWindows(section, chunks);
                continue;
            }

            if (group.Count > 0 && groupLength + 1 + section.Text.Length > maxChunkChars)
            {
                EmitGroup(group, runParent, chunks);
                group.Clear();
                groupLength = 0;
            }

            group.Add(section);
            groupLength = group.Count == 1 ? section.Text.Length : groupLength + 1 + section.Text.Length;
        }

        EmitGroup(group, runParent, chunks);
    }

    private void EmitGroup(List<DocumentSection> group, string runParent, List<ChunkDraft> chunks)
    {
        if (group.Count == 0)
            return;

        var text = group.Count == 1 ? group[0].Text : string.Join('\n', group.Select(s => s.Text));
        var path = group.Count == 1 ? group[0].SectionPath : runParent;
        chunks.Add(new ChunkDraft(
            SectionPath: path,
            Text: text,
            PageStart: SpanMin(group, s => s.PageStart),
            PageEnd: SpanMax(group, s => s.PageEnd),
            LineStart: SpanMin(group, s => s.LineStart),
            LineEnd: SpanMax(group, s => s.LineEnd)));
    }

    private void EmitWindows(DocumentSection section, List<ChunkDraft> chunks)
    {
        var units = TextUnitSplitter.Split(section.Text);
        foreach (var window in TextWindowPacker.Pack(units, maxChunkChars, overlapChars))
        {
            chunks.Add(new ChunkDraft(
                SectionPath: section.SectionPath,
                Text: window,
                PageStart: section.PageStart,
                PageEnd: section.PageEnd,
                // The window is a slice of the section; per-window line offsets cannot be
                // mapped back to file lines when blank lines separate paragraphs, so the whole
                // section span is kept on every window.
                LineStart: section.LineStart,
                LineEnd: section.LineEnd));
        }
    }

    private static string ParentOf(string sectionPath)
    {
        var separator = sectionPath.LastIndexOf(" > ", StringComparison.Ordinal);
        return separator < 0 ? string.Empty : sectionPath[..separator];
    }

    private static int? SpanMin(IEnumerable<DocumentSection> sections, Func<DocumentSection, int?> pick)
    {
        int? result = null;
        foreach (var section in sections)
        {
            var value = pick(section);
            if (value.HasValue && (result is null || value < result))
                result = value;
        }

        return result;
    }

    private static int? SpanMax(IEnumerable<DocumentSection> sections, Func<DocumentSection, int?> pick)
    {
        int? result = null;
        foreach (var section in sections)
        {
            var value = pick(section);
            if (value.HasValue && (result is null || value > result))
                result = value;
        }

        return result;
    }
}
