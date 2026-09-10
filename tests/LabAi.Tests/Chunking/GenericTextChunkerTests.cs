using LabAi.Application.Chunking;
using LabAi.Domain.Enums;
using LabAi.Domain.ValueObjects;

namespace LabAi.Tests.Chunking;

public sealed class GenericTextChunkerTests
{
    private readonly GenericTextChunker chunker = new();

    [Fact]
    public void SectionWithinBudgetStaysOneChunk()
    {
        var text = "sample_id: BAL-001\nowner: ivanov@example.ru\nstatus: OK";
        var document = new ParsedDocument([new DocumentSection("Json", text)]);

        var chunks = chunker.Chunk(document);

        chunks.Should().HaveCount(1);
        chunks[0].SectionPath.Should().Be("Json");
        chunks[0].Text.Should().Be(text);
    }

    [Fact]
    public void OversizedSectionSplitsOnLineBoundariesWithOverlap()
    {
        var lines = new[]
        {
            new string('a', 40), new string('b', 40), new string('c', 40), new string('d', 40), new string('e', 40),
        };
        var chunker = new GenericTextChunker(maxChunkChars: 150, overlapChars: 40);
        var document = new ParsedDocument([new DocumentSection("Json", string.Join('\n', lines))]);

        var chunks = chunker.Chunk(document);

        chunks.Should().HaveCount(2);
        chunks[0].Text.Should().Be($"{lines[0]}\n{lines[1]}\n{lines[2]}");
        chunks[1].Text.Should().Be($"{lines[2]}\n{lines[3]}\n{lines[4]}");
        chunks.Select(c => c.Text.Length).Should().OnlyContain(length => length <= 150);
    }

    [Fact]
    public void SectionsAreNeverMerged()
    {
        var document = new ParsedDocument(
        [
            new DocumentSection("[0]", "sample_id: A"),
            new DocumentSection("[1]", "sample_id: B"),
        ]);

        var chunks = chunker.Chunk(document);

        chunks.Should().HaveCount(2);
        chunks[0].SectionPath.Should().Be("[0]");
        chunks[1].SectionPath.Should().Be("[1]");
    }

    [Fact]
    public void EmptyDocumentProducesNoChunks()
    {
        chunker.Chunk(ParsedDocument.Empty).Should().BeEmpty();
    }

    [Fact]
    public void SupportsJsonAndPdfKinds()
    {
        chunker.SupportedKinds.Should().Equal(DocumentKind.Json, DocumentKind.Pdf);
    }
}
