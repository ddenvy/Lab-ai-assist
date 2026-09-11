using LabAi.Application.Chunking;
using LabAi.Domain.ValueObjects;

namespace LabAi.Tests.Chunking;

public sealed class MarkdownChunkerTests
{
    private readonly MarkdownChunker chunker = new();

    [Fact]
    public void SectionsWithinBudgetStayOneChunkEach()
    {
        var document = new ParsedDocument(
        [
            new DocumentSection("SOP-QC-001 > 1. Purpose", "First section.", LineStart: 1, LineEnd: 1),
            new DocumentSection("SOP-QC-002 > 2. Scope", "Second section.", LineStart: 5, LineEnd: 5),
        ]);

        var chunks = chunker.Chunk(document);

        chunks.Should().HaveCount(2);
        chunks[0].SectionPath.Should().Be("SOP-QC-001 > 1. Purpose");
        chunks[0].Text.Should().Be("First section.");
        chunks[0].LineStart.Should().Be(1);
        chunks[1].SectionPath.Should().Be("SOP-QC-002 > 2. Scope");
    }

    [Fact]
    public void AdjacentSiblingSectionsMergeUnderCommonParentPath()
    {
        var document = new ParsedDocument(
        [
            new DocumentSection("SOP > 4 > 4.1 Flow Rate", "Flow 1.0 mL/min.", LineStart: 3, LineEnd: 4),
            new DocumentSection("SOP > 4 > 4.2 Temperature", "Column 30 C.", LineStart: 6, LineEnd: 7),
        ]);

        var chunks = chunker.Chunk(document);

        chunks.Should().HaveCount(1);
        chunks[0].SectionPath.Should().Be("SOP > 4");
        chunks[0].Text.Should().Be("Flow 1.0 mL/min.\nColumn 30 C.");
        chunks[0].LineStart.Should().Be(3);
        chunks[0].LineEnd.Should().Be(7);
    }

    [Fact]
    public void MergeStopsWhenTheCombinedTextExceedsTheBudget()
    {
        var chunker = new MarkdownChunker(maxChunkChars: 100, overlapChars: 20);
        var document = new ParsedDocument(
        [
            new DocumentSection("SOP > A", new string('x', 60)),
            new DocumentSection("SOP > B", new string('y', 60)),
        ]);

        var chunks = chunker.Chunk(document);

        chunks.Should().HaveCount(2);
        chunks[0].SectionPath.Should().Be("SOP > A");
        chunks[1].SectionPath.Should().Be("SOP > B");
        chunks.Select(c => c.Text.Length).Should().OnlyContain(length => length <= 100);
    }

    [Fact]
    public void SectionsWithDifferentParentsNeverMerge()
    {
        var document = new ParsedDocument(
        [
            new DocumentSection("SOP1 > A", "Text one."),
            new DocumentSection("SOP2 > B", "Text two."),
        ]);

        var chunks = chunker.Chunk(document);

        chunks.Should().HaveCount(2);
    }

    [Fact]
    public void PathlessSectionsMergeIntoAPathlessChunk()
    {
        var document = new ParsedDocument(
        [
            new DocumentSection("", "Introductory paragraph."),
            new DocumentSection("", "Second paragraph."),
        ]);

        var chunks = chunker.Chunk(document);

        chunks.Should().HaveCount(1);
        chunks[0].SectionPath.Should().BeEmpty();
        chunks[0].Text.Should().Be("Introductory paragraph.\nSecond paragraph.");
    }

    [Fact]
    public void OversizedSectionSplitsOnSentenceBoundariesWithRollingOverlap()
    {
        const string first = "First short sample sentence.";
        const string second = "Second short sample sentence.";
        const string third = "Third sample sentence, the longest one of all.";
        var chunker = new MarkdownChunker(maxChunkChars: 80, overlapChars: 30);
        var document = new ParsedDocument([new DocumentSection("SOP > 1", $"{first}\n{second}\n{third}")]);

        var chunks = chunker.Chunk(document);

        chunks.Should().HaveCount(2);
        chunks[0].Text.Should().Be($"{first}\n{second}");
        chunks[0].Text.Length.Should().BeLessThanOrEqualTo(80);
        chunks[1].Text.Should().Be($"{second}\n{third}");
        chunks[1].Text.Length.Should().BeLessThanOrEqualTo(80);
    }

    [Fact]
    public void SentenceLongerThanTheBudgetIsHardSplitWithoutLoss()
    {
        var text = new string('x', 150);
        var chunker = new MarkdownChunker(maxChunkChars: 100, overlapChars: 20);
        var document = new ParsedDocument([new DocumentSection("SOP > 1", text)]);

        var chunks = chunker.Chunk(document);

        chunks.Should().HaveCount(2);
        chunks.Select(c => c.Text.Length).Should().Equal(100, 50);
        string.Concat(chunks.Select(c => c.Text)).Should().Be(text);
    }

    [Fact]
    public void DecimalPointsDoNotEndASentence()
    {
        var chunker = new MarkdownChunker(maxChunkChars: 20, overlapChars: 5);
        var document = new ParsedDocument(
            [new DocumentSection("SOP > 1", "RSD limit 2.0%. Repeat three times.")]);

        var chunks = chunker.Chunk(document);

        // "2.0%" must stay inside one unit; the split happens only after "2.0%.".
        chunks.Should().HaveCount(2);
        chunks[0].Text.Should().Be("RSD limit 2.0%.");
        chunks[1].Text.Should().Be("Repeat three times.");
    }

    [Fact]
    public void AbbreviationFollowedByANumberStaysInOneSentence()
    {
        var chunker = new MarkdownChunker(maxChunkChars: 12, overlapChars: 2);
        const string text = "Sect. 4.2 of law.";
        var document = new ParsedDocument([new DocumentSection("SOP > 1", text)]);

        var chunks = chunker.Chunk(document);

        // One 17-char sentence over the budget of 12, so it is hard-split at the budget boundary;
        // a naive split at "Sect." would produce whole-word windows instead.
        chunks.Select(c => c.Text.Length).Should().Equal(12, 5);
        chunks[0].Text.Should().Be("Sect. 4.2 of");
        string.Concat(chunks.Select(c => c.Text)).Should().Be(text);
    }

    [Fact]
    public void EmptyDocumentProducesNoChunks()
    {
        chunker.Chunk(ParsedDocument.Empty).Should().BeEmpty();
    }
}
