using LabAi.Application.Chunking;
using LabAi.Domain.Enums;
using LabAi.Domain.ValueObjects;

namespace LabAi.Tests.Chunking;

public sealed class CsvRowGroupChunkerTests
{
    private const string Header = "Sample Name,Peak,Area";

    private readonly CsvRowGroupChunker chunker = new();

    [Fact]
    public void SampleGroupFitsInOneChunkWithPreambleAndRepeatedHeader()
    {
        var document = new ParsedDocument(
        [
            new DocumentSection(
                "Sample_003",
                $"{Header}\nSample_003,1,100.5\nSample_003,2,150.2",
                LineStart: 2,
                LineEnd: 3),
        ]);

        var chunks = chunker.Chunk(document);

        chunks.Should().HaveCount(1);
        chunks[0].SectionPath.Should().Be("Sample_003");
        chunks[0].Text.Should().Be(
            "Результаты измерений, образец Sample_003: строк данных 2.\n" +
            $"{Header}\nSample_003,1,100.5\nSample_003,2,150.2");
        chunks[0].LineStart.Should().Be(2);
        chunks[0].LineEnd.Should().Be(3);
    }

    [Fact]
    public void FallbackSectionPreambleHasNoSamplePhrase()
    {
        var document = new ParsedDocument(
        [
            new DocumentSection(
                DocumentSection.UngroupedRowsPath,
                $"{Header}\nRow one\nRow two\nRow three"),
        ]);

        var chunks = chunker.Chunk(document);

        chunks.Should().HaveCount(1);
        chunks[0].Text.Should().StartWith("Результаты измерений: строк данных 3.\n");
        chunks[0].Text.Should().NotContain("образец");
    }

    [Fact]
    public void OversizedGroupSplitsIntoRowWindowsRepeatingPreambleAndHeader()
    {
        var rows = new[] { new string('a', 24), new string('b', 24), new string('c', 24), new string('d', 24) };
        var chunker = new CsvRowGroupChunker(maxChunkChars: 150);
        var document = new ParsedDocument(
        [
            new DocumentSection("Sample_003", Header + "\n" + string.Join('\n', rows), LineStart: 2),
        ]);

        var chunks = chunker.Chunk(document);

        // rowBudget = 150 - 57 (preamble) - 1 - 21 (header) - 1 = 70; two 24-char rows fit, the
        // third pair does not, so rows split 2 + 2.
        chunks.Should().HaveCount(2);
        foreach (var chunk in chunks)
        {
            chunk.Text.Should().StartWith("Результаты измерений, образец Sample_003: строк данных 4.\n" + Header + "\n");
            chunk.Text.Length.Should().BeLessThanOrEqualTo(150);
        }

        chunks[0].Text.Should().EndWith("\n" + rows[0] + "\n" + rows[1]);
        chunks[1].Text.Should().EndWith("\n" + rows[2] + "\n" + rows[3]);
    }

    [Fact]
    public void RowsAreNeverOverlapped()
    {
        var rows = new[] { new string('a', 24), new string('b', 24), new string('c', 24), new string('d', 24) };
        var chunker = new CsvRowGroupChunker(maxChunkChars: 150);
        var document = new ParsedDocument(
            [new DocumentSection("Sample_003", Header + "\n" + string.Join('\n', rows))]);

        var chunks = chunker.Chunk(document);

        var emittedRows = chunks
            .SelectMany(c => c.Text.Split('\n').Skip(2)) // preamble + repeated header first
            .ToList();
        emittedRows.Should().Equal(rows);
    }

    [Fact]
    public void RowWindowLineSpansTrackTheirRows()
    {
        var rows = new[] { new string('a', 24), new string('b', 24), new string('c', 24), new string('d', 24) };
        var chunker = new CsvRowGroupChunker(maxChunkChars: 150);
        var document = new ParsedDocument(
            [new DocumentSection("Sample_003", Header + "\n" + string.Join('\n', rows), LineStart: 2)]);

        var chunks = chunker.Chunk(document);

        chunks[0].LineStart.Should().Be(2);
        chunks[0].LineEnd.Should().Be(3);
        chunks[1].LineStart.Should().Be(4);
        chunks[1].LineEnd.Should().Be(5);
    }

    [Fact]
    public void HeaderOnlySectionProducesNoChunks()
    {
        var document = new ParsedDocument([new DocumentSection("Sample_003", Header)]);

        chunker.Chunk(document).Should().BeEmpty();
    }

    [Fact]
    public void NullLineCoordinatesStayNull()
    {
        var document = new ParsedDocument(
            [new DocumentSection("Sample_003", $"{Header}\nSample_003,1,100.5")]);

        var chunks = chunker.Chunk(document);

        chunks[0].LineStart.Should().BeNull();
        chunks[0].LineEnd.Should().BeNull();
    }

    [Fact]
    public void SupportsOnlyTheCsvKind()
    {
        chunker.SupportedKinds.Should().Equal(DocumentKind.Csv);
    }
}
