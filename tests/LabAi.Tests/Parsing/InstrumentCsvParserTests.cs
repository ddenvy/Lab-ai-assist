using System.Text;
using LabAi.Domain.Enums;
using LabAi.Infrastructure.Parsing;

namespace LabAi.Tests.Parsing;

/// <summary>
/// The fixture mirrors the real chromatography export <c>Mini-CDS\Reports\1.csv</c>: UTF-8 BOM,
/// CRLF line endings, one row per peak, and a <c>Tailing</c> column that is sometimes empty.
/// </summary>
public sealed class InstrumentCsvParserTests
{
    private const string HeaderLine =
        "Sample Name,Method Name,Status,Created At,Peak #,Retention Time (s),Height,Area,FWHM,Plates,Tailing";

    private readonly InstrumentCsvParser parser = new();

    [Fact]
    public void GroupsRowsBySampleName_IntoOneSectionPerSample()
    {
        var csv = Bom() + HeaderLine + "\r\n" +
                  "Sample_003,Standard Analysis,Completed,2026-09-09T12:54:44Z,1,1.900,1.26,1.18,0.877,26,\r\n" +
                  "Sample_003,Standard Analysis,Completed,2026-09-09T12:54:44Z,2,2.200,1.27,1.18,0.877,35,\r\n" +
                  "Sample_007,Standard Analysis,Completed,2026-09-09T13:01:10Z,1,1.850,1.30,1.20,0.880,28,\r\n";

        var parsed = parser.Parse(Utf8(csv));

        parsed.Sections.Select(s => s.SectionPath).Should().Equal(["Sample_003", "Sample_007"]);
        parsed.Sections[0].Text.Should().StartWith(HeaderLine, "the chunker needs column names for its preamble");
        parsed.Sections[0].Text.Split('\n').Should().HaveCount(3, "header + two peaks");
    }

    [Fact]
    public void PreservesTheOrderOfFirstAppearance()
    {
        var csv = HeaderLine + "\n" +
                  "Sample_B,m,Completed,2026-09-09T12:00:00Z,1,1.9,1,1,1,10,\n" +
                  "Sample_A,m,Completed,2026-09-09T12:00:00Z,1,1.9,1,1,1,10,\n" +
                  "Sample_B,m,Completed,2026-09-09T12:00:00Z,2,2.1,1,1,1,10,\n";

        var parsed = parser.Parse(Utf8(csv));

        parsed.Sections.Select(s => s.SectionPath).Should().Equal(["Sample_B", "Sample_A"]);
    }

    [Fact]
    public void KeepsTheRawRowText_IncludingTheEmptyTrailingCell()
    {
        var csv = HeaderLine + "\n" +
                  "Sample_003,m,Completed,2026-09-09T12:54:44Z,1,1.900,1.26,1.18,0.877,26,\n";

        var parsed = parser.Parse(Utf8(csv));

        // The empty Tailing cell must survive verbatim: re-serialising or dropping it would make
        // the stored chunk text diverge from the file on disk, which is the auditable original.
        parsed.Sections.Should().ContainSingle();
        parsed.Sections[0].Text.Should().EndWith("0.877,26,");
    }

    [Fact]
    public void HandlesQuotedFieldsWithCommasAndEscapedQuotes()
    {
        var csv = """
            Sample Name,Note
            "Sample, 3","Peak ""5"" is solvent front"
            """;

        var parsed = parser.Parse(Utf8(csv));

        parsed.Sections[0].SectionPath.Should().Be("Sample, 3");
    }

    [Fact]
    public void RejectsARowWithAMisalignedColumnCount()
    {
        var csv = HeaderLine + "\n" +
                  "Sample_003,Standard Analysis,Completed,2026-09-09T12:54:44Z,1,1.900,1.26,1.18,0.877\n";

        var act = () => parser.Parse(Utf8(csv));

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*line 2*expected 11 columns, found 9*");
    }

    [Fact]
    public void RejectsAnUnterminatedQuote()
    {
        var csv = "Sample Name,Note\n" +
                  "\"Sample_003,unterminated\n";

        var act = () => parser.Parse(Utf8(csv));

        act.Should().Throw<InvalidDataException>().WithMessage("*line 2*unterminated*");
    }

    [Fact]
    public void FallsBackToASingleSection_WhenThereIsNoSampleNameColumn()
    {
        var csv = "Timestamp,Value\n" +
                  "2026-09-09T12:00:00Z,21.4\n";

        var parsed = parser.Parse(Utf8(csv));

        parsed.Sections.Should().ContainSingle();
        parsed.Sections[0].SectionPath.Should().Be("Rows");
        parsed.Sections[0].Text.Should().Contain("21.4");
    }

    [Fact]
    public void StripsUtf8ByteOrderMark_FromTheSampleName()
    {
        // Without BOM removal the first group key would be "\uFEFFSample_003" — a distinct,
        // invisible sample that never matches golden-set expectations.
        var csv = Bom() + HeaderLine + "\n" +
                  "Sample_003,m,Completed,2026-09-09T12:54:44Z,1,1.900,1.26,1.18,0.877,26,\n";

        var parsed = parser.Parse(Utf8(csv));

        parsed.Sections.Single().SectionPath.Should().Be("Sample_003");
    }

    [Fact]
    public void EmptyContent_ProducesNoSections()
    {
        parser.Parse([]).Sections.Should().BeEmpty();
    }

    [Fact]
    public void DeclaresTheCsvKind()
    {
        parser.Kind.Should().Be(DocumentKind.Csv);
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static string Bom() => Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());
}
