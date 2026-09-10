using LabAi.Domain.Abstractions;
using LabAi.Domain.Enums;
using LabAi.Domain.ValueObjects;
using UglyToad.PdfPig;

namespace LabAi.Infrastructure.Parsing;

/// <summary>
/// Parses PDF pages into sections, one per page, using PdfPig. PDF has no line concept, so the
/// section carries page numbers instead of line spans; the text is reconstructed from words grouped
/// by baseline, because <c>page.Text</c> flattens layout into a space-joined stream. Pages with no
/// extractable text — covers, scans — are skipped rather than becoming empty chunks. Standard
/// Unicode ligatures are expanded because a stored <c>ﬁ</c> would never match the query
/// <c>fi</c> at retrieval time.
/// </summary>
public sealed class PdfPigPdfParser : IDocumentParser
{
    private static readonly (string Ligature, string Expanded)[] Ligatures =
    [
        ("ﬀ", "ff"), ("ﬁ", "fi"), ("ﬂ", "fl"), ("ﬃ", "ffi"), ("ﬄ", "ffl"), ("ﬅ", "ft")
    ];

    public DocumentKind Kind => DocumentKind.Pdf;

    public ParsedDocument Parse(byte[] content)
    {
        using var document = PdfDocument.Open(content);

        var sections = new List<DocumentSection>();
        foreach (var page in document.GetPages())
        {
            var text = ExpandLigatures(ExtractLines(page));
            if (text.Length == 0)
                continue;

            sections.Add(new DocumentSection(
                SectionPath: $"Page {page.Number}",
                Text: text,
                PageStart: page.Number,
                PageEnd: page.Number));
        }

        return new ParsedDocument(sections);
    }

    private static string ExpandLigatures(string text)
    {
        foreach (var (ligature, expanded) in Ligatures)
            text = text.Replace(ligature, expanded, StringComparison.Ordinal);

        return text;
    }

    private static string ExtractLines(UglyToad.PdfPig.Content.Page page)
    {
        // Baselines can differ by fractions of a point for glyphs on the same visual line, so
        // grouping rounds to whole points; sub-point jitter within one line never crosses a
        // full-point boundary in practice for documents produced by word processors.
        var lines = page.GetWords()
            .GroupBy(word => Math.Round(word.BoundingBox.Bottom, MidpointRounding.AwayFromZero))
            .OrderByDescending(group => group.Key)
            .Select(group => string.Join(' ', group.OrderBy(word => word.BoundingBox.Left).Select(word => word.Text)));

        return string.Join('\n', lines);
    }
}
