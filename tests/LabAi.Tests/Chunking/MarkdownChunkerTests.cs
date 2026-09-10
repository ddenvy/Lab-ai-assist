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
            new DocumentSection("SOP-QC-001 > 1. Назначение", "Раздел первый.", LineStart: 1, LineEnd: 1),
            new DocumentSection("SOP-QC-002 > 2. Область", "Раздел второй.", LineStart: 5, LineEnd: 5),
        ]);

        var chunks = chunker.Chunk(document);

        chunks.Should().HaveCount(2);
        chunks[0].SectionPath.Should().Be("SOP-QC-001 > 1. Назначение");
        chunks[0].Text.Should().Be("Раздел первый.");
        chunks[0].LineStart.Should().Be(1);
        chunks[1].SectionPath.Should().Be("SOP-QC-002 > 2. Область");
    }

    [Fact]
    public void AdjacentSiblingSectionsMergeUnderCommonParentPath()
    {
        var document = new ParsedDocument(
        [
            new DocumentSection("SOP > 4 > 4.1 Скорость", "Поток 1.0 мл/мин.", LineStart: 3, LineEnd: 4),
            new DocumentSection("SOP > 4 > 4.2 Температура", "Колонка 30 C.", LineStart: 6, LineEnd: 7),
        ]);

        var chunks = chunker.Chunk(document);

        chunks.Should().HaveCount(1);
        chunks[0].SectionPath.Should().Be("SOP > 4");
        chunks[0].Text.Should().Be("Поток 1.0 мл/мин.\nКолонка 30 C.");
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
            new DocumentSection("SOP1 > A", "Текст один."),
            new DocumentSection("SOP2 > B", "Текст два."),
        ]);

        var chunks = chunker.Chunk(document);

        chunks.Should().HaveCount(2);
    }

    [Fact]
    public void PathlessSectionsMergeIntoAPathlessChunk()
    {
        var document = new ParsedDocument(
        [
            new DocumentSection("", "Вводный абзац."),
            new DocumentSection("", "Второй абзац."),
        ]);

        var chunks = chunker.Chunk(document);

        chunks.Should().HaveCount(1);
        chunks[0].SectionPath.Should().BeEmpty();
        chunks[0].Text.Should().Be("Вводный абзац.\nВторой абзац.");
    }

    [Fact]
    public void OversizedSectionSplitsOnSentenceBoundariesWithRollingOverlap()
    {
        const string first = "Первое предложение с текстом.";
        const string second = "Второе предложение подлиннее.";
        const string third = "Третье предложение самое длинное из всех.";
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
            [new DocumentSection("SOP > 1", "Предел RSD 2.0%. Повторить трижды.")]);

        var chunks = chunker.Chunk(document);

        // "2.0%" must stay inside one unit; the split happens only after "2.0%.".
        chunks.Should().HaveCount(2);
        chunks[0].Text.Should().Be("Предел RSD 2.0%.");
        chunks[1].Text.Should().Be("Повторить трижды.");
    }

    [Fact]
    public void AbbreviationFollowedByANumberStaysInOneSentence()
    {
        var chunker = new MarkdownChunker(maxChunkChars: 12, overlapChars: 2);
        const string text = "Раздел п. 4.2 закона.";
        var document = new ParsedDocument([new DocumentSection("SOP > 1", text)]);

        var chunks = chunker.Chunk(document);

        // One 21-char sentence over the budget of 12, so it is hard-split mid-word; a naive
        // split at "п." would produce whole-word windows instead.
        chunks.Select(c => c.Text.Length).Should().Equal(12, 9);
        chunks[0].Text.Should().Be("Раздел п. 4.");
        string.Concat(chunks.Select(c => c.Text)).Should().Be(text);
    }

    [Fact]
    public void EmptyDocumentProducesNoChunks()
    {
        chunker.Chunk(ParsedDocument.Empty).Should().BeEmpty();
    }
}
