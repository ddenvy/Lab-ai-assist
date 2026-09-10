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
            "Measurement results, sample Sample_003: 2 data rows.\n" +
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
        chunks[0].Text.Should().StartWith("Measurement results: 3 data rows.\n");
        chunks[0].Text.Should().NotContain("sample ");
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

        // rowBudget = 150 - 52 (preamble) - 1 - 21 (header) - 1 = 75; three 24-char rows fit
        // (72 + 2 separators = 74), the fourth does not, so rows split 3 + 1.
        chunks.Should().HaveCount(2);
        foreach (var chunk in chunks)
        {
            chunk.Text.Should().StartWith("Measurement results, sample Sample_003: 4 data rows.\n" + Header + "\n");
            chunk.Text.Length.Should().BeLessThanOrEqualTo(150);
        }

        chunks[0].Text.Should().EndWith("\n" + rows[0] + "\n" + rows[1] + "\n" + rows[2]);
        chunks[1].Text.Should().EndWith("\n" + rows[3]);
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
        chunks[0].LineEnd.Should().Be(4); // rows 0,1,2 → source lines 2,3,4
        chunks[1].LineStart.Should().Be(5); // row 3 → source line 5
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
