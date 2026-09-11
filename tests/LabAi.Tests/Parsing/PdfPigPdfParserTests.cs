using LabAi.Domain.Enums;
using LabAi.Infrastructure.Parsing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace LabAi.Tests.Parsing;

/// <summary>
/// QuestPDF builds the fixture in memory, so the test exercises a real PDF through PdfPig without
/// a binary fixture in the repository. QuestPDF is a test-only dependency: product code parses PDFs,
/// it never creates them.
/// </summary>
public sealed class PdfPigPdfParserTests
{
    static PdfPigPdfParserTests() => QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    private readonly PdfPigPdfParser parser = new();

    [Fact]
    public void SplitsAPdfIntoOneSectionPerPage()
    {
        var parsed = parser.Parse(TwoPagePdf());

        parsed.Sections.Select(s => s.SectionPath).Should().Equal(["Page 1", "Page 2"]);
    }

    [Fact]
    public void ReportsThePageNumberOfEachSection()
    {
        var parsed = parser.Parse(TwoPagePdf());

        parsed.Sections[0].PageStart.Should().Be(1);
        parsed.Sections[0].PageEnd.Should().Be(1);
        parsed.Sections[1].PageStart.Should().Be(2);
        parsed.Sections[1].PageEnd.Should().Be(2);
        parsed.Sections[0].LineStart.Should().BeNull("PDF has no line concept; page numbers are the citation anchor");
    }

    [Fact]
    public void ExtractsTheTextOfEachPage()
    {
        var parsed = parser.Parse(TwoPagePdf());

        // Keep the Latin fixture free of the "ti" bigram on purpose: QuestPDF's default font
        // renders "ti" as a discretionary ligature with no Unicode mapping, so PdfPig loses
        // those glyphs entirely — a font artifact, not a parser one.
        parsed.Sections[0].Text.Should().Contain("suitability");
        parsed.Sections[0].Text.Should().NotContain("balances");
        parsed.Sections[1].Text.Should().Contain("balances");
        parsed.Sections[1].Text.Should().NotContain("suitability");
    }

    [Fact]
    public void PreservesLineStructureWithinAPage()
    {
        var content = Document.Create(container =>
            container.Page(page =>
            {
                page.Margin(36);
                page.Content().Column(column =>
                {
                    column.Item().Text("Primary check line.");
                    column.Item().Text("Secondary check line.");
                });
            })).GeneratePdf();

        var parsed = parser.Parse(content);

        var lines = parsed.Sections.Single().Text.Split('\n');
        lines.Should().Contain(l => l.StartsWith("Primary check"));
        lines.Should().Contain(l => l.StartsWith("Secondary check"));
    }

    [Fact]
    public void DeclaresThePdfKind()
    {
        parser.Kind.Should().Be(DocumentKind.Pdf);
    }

    private static byte[] TwoPagePdf() => Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.Content().Text("System suitability criteria for HPLC.");
        });

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.Content().Text("Daily check of lab balances.");
        });
    }).GeneratePdf();
}
