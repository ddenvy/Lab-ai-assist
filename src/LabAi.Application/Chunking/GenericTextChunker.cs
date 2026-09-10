using LabAi.Domain.Abstractions;
using LabAi.Domain.Enums;
using LabAi.Domain.ValueObjects;

namespace LabAi.Application.Chunking;

/// <summary>
/// Fallback chunker for plain-text-like parsed documents — flattened JSON (the JSON parser emits
/// one section per root element, each line a <c>path: value</c> pair) and PDF pages (plain text
/// per page once parsed). Sections are windowed independently on line boundaries with a rolling
/// overlap; sections are deliberately not merged, because a JSON array element or a PDF page is
/// a natural retrieval unit whose path must stay intact.
/// </summary>
public sealed class GenericTextChunker : IChunkingStrategy
{
    private readonly int maxChunkChars;
    private readonly int overlapChars;

    public GenericTextChunker(int maxChunkChars = 2000, int overlapChars = 200)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChunkChars, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(overlapChars, maxChunkChars);

        this.maxChunkChars = maxChunkChars;
        this.overlapChars = overlapChars;
    }

    public IEnumerable<DocumentKind> SupportedKinds => [DocumentKind.Json, DocumentKind.Pdf];

    public IReadOnlyList<ChunkDraft> Chunk(ParsedDocument document)
    {
        var chunks = new List<ChunkDraft>();

        foreach (var section in document.Sections)
        {
            if (string.IsNullOrWhiteSpace(section.Text))
                continue;

            var units = section.Text.Split(['\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var window in TextWindowPacker.Pack(units, maxChunkChars, overlapChars))
            {
                chunks.Add(new ChunkDraft(
                    SectionPath: section.SectionPath,
                    Text: window,
                    PageStart: section.PageStart,
                    PageEnd: section.PageEnd,
                    LineStart: section.LineStart,
                    LineEnd: section.LineEnd));
            }
        }

        return chunks;
    }
}
