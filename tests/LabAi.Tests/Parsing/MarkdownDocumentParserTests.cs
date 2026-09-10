using System.Text;
using LabAi.Domain.Enums;
using LabAi.Infrastructure.Parsing;

namespace LabAi.Tests.Parsing;

public sealed class MarkdownDocumentParserTests
{
    private readonly MarkdownDocumentParser parser = new();

    [Fact]
    public void SplitsSectionsByHeadings_AndBuildsTheStack()
    {
        var parsed = parser.Parse(Md("""
            # SOP-QC-001 HPLC

            Intro text under the title.

            ## 4. System suitability

            RSD must not exceed 2.0%.

            ### 4.2 Acceptance criteria

            Theoretical plates >= 2000.
            """));

        parsed.Sections.Select(s => s.SectionPath).Should().Equal(
            ["SOP-QC-001 HPLC",
             "SOP-QC-001 HPLC > 4. System suitability",
             "SOP-QC-001 HPLC > 4. System suitability > 4.2 Acceptance criteria"]);
        parsed.Sections.Select(s => s.Text.Trim()).Should().Contain(t => t.StartsWith("Theoretical plates"));
    }

    [Fact]
    public void StripsYamlFrontMatter_SoItNeverBecomesAChunk()
    {
        var parsed = parser.Parse(Md("""
            ---
            doc_id: SOP-QC-001
            title: HPLC system suitability
            version: 3
            ---

            # Body

            Content.
            """));

        parsed.Sections.Should().ContainSingle();
        parsed.Sections[0].SectionPath.Should().Be("Body");
        parsed.Sections[0].Text.Should().NotContain("doc_id");
    }

    [Fact]
    public void UnterminatedFrontMatter_IsTreatedAsBody()
    {
        var parsed = parser.Parse(Md("""
            ---
            doc_id: SOP-QC-001

            # Body

            Content.
            """));

        // The leading --- is a horizontal rule, so the would-be front-matter stays ordinary body
        // content — visible in a chunk, not silently swallowed.
        parsed.Sections.Should().HaveCount(2);
        parsed.Sections[0].SectionPath.Should().BeEmpty();
        parsed.Sections[0].Text.Should().Contain("doc_id: SOP-QC-001");
        parsed.Sections[1].SectionPath.Should().Be("Body");
    }

    [Fact]
    public void HashInsideCodeFence_IsContentNotStructure()
    {
        var parsed = parser.Parse(Md("""
            # Guide

            ```bash
            # this comment is not a heading
            ```
            """));

        parsed.Sections.Should().ContainSingle();
        parsed.Sections[0].SectionPath.Should().Be("Guide");
        parsed.Sections[0].Text.Should().Contain("# this comment is not a heading");
    }

    [Fact]
    public void ReportsInclusiveOneBasedLineSpans()
    {
        var parsed = parser.Parse(Md("""
            # Title

            line one

            ## Second

            line two
            """));

        parsed.Sections[0].LineStart.Should().Be(3, "the first non-blank line after '# Title'");
        parsed.Sections[0].LineEnd.Should().Be(3);
        parsed.Sections[1].LineStart.Should().Be(7, "the blank line after '## Second' is padding, not content");
        parsed.Sections[1].LineEnd.Should().Be(7);
    }

    [Fact]
    public void HandlesCrlfLineEndings()
    {
        var content = Encoding.UTF8.GetBytes("# Title\r\n\r\nBody text\r\n");
        var parsed = parser.Parse(content);

        parsed.Sections.Should().ContainSingle();
        parsed.Sections[0].Text.Should().Be("Body text");
    }

    [Fact]
    public void StripsUtf8ByteOrderMark()
    {
        byte[] content = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("# Title\n\nBody\n")];
        var parsed = parser.Parse(content);

        parsed.Sections[0].SectionPath.Should().Be("Title");
    }

    [Fact]
    public void ContentBeforeTheFirstHeading_BecomesADocumentLevelSection()
    {
        var parsed = parser.Parse(Md("""
            Preamble without any heading.

            # Title

            Body.
            """));

        parsed.Sections.Should().HaveCount(2);
        parsed.Sections[0].SectionPath.Should().BeEmpty();
    }

    [Fact]
    public void EmptyContent_ProducesNoSections()
    {
        parser.Parse([]).Sections.Should().BeEmpty();
    }

    [Fact]
    public void DeclaresTheMarkdownKind()
    {
        parser.Kind.Should().Be(DocumentKind.Markdown);
    }

    private static byte[] Md(string text) => Encoding.UTF8.GetBytes(text);
}
