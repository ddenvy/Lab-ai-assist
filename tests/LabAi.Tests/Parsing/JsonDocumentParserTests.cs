using System.Text;
using LabAi.Domain.Enums;
using LabAi.Infrastructure.Parsing;

namespace LabAi.Tests.Parsing;

public sealed class JsonDocumentParserTests
{
    private readonly JsonDocumentParser parser = new();

    [Fact]
    public void FlattensAnObjectIntoPathValueLines()
    {
        var parsed = parser.Parse(Utf8("""
            {
              "instrument": "Agilent 1260",
              "log": { "date": "2026-09-09", "result": "pass" }
            }
            """));

        parsed.Sections.Should().ContainSingle();
        parsed.Sections[0].SectionPath.Should().Be("Json");
        parsed.Sections[0].Text.Should().Be(
            "Json.instrument: Agilent 1260\nJson.log.date: 2026-09-09\nJson.log.result: pass");
    }

    [Fact]
    public void SplitsARootArrayIntoOneSectionPerElement()
    {
        var parsed = parser.Parse(Utf8("""
            [
              { "run": 1, "status": "pass" },
              { "run": 2, "status": "fail" }
            ]
            """));

        parsed.Sections.Select(s => s.SectionPath).Should().Equal(["[0]", "[1]"]);
        parsed.Sections[1].Text.Should().Be("[1].run: 2\n[1].status: fail");
    }

    [Fact]
    public void RendersArraysAsIndexedPaths()
    {
        var parsed = parser.Parse(Utf8("""
            { "plateaus": [2000, 2500] }
            """));

        parsed.Sections[0].Text.Should().Contain("Json.plateaus[0]: 2000");
        parsed.Sections[0].Text.Should().Contain("Json.plateaus[1]: 2500");
    }

    [Fact]
    public void SkipsNullsAndEmptyContainers()
    {
        var parsed = parser.Parse(Utf8("""
            {
              "owner": null,
              "notes": [],
              "meta": {},
              "value": 5
            }
            """));

        parsed.Sections[0].Text.Should().Be("Json.value: 5");
    }

    [Fact]
    public void CollapsesLineBreaksInsideStringValues()
    {
        var parsed = parser.Parse(Utf8("""
            { "comment": "line one\nline two" }
            """));

        parsed.Sections[0].Text.Should().NotContain("\n").And.Contain("line one line two");
    }

    [Fact]
    public void RejectsInvalidJson()
    {
        var act = () => parser.Parse(Utf8("{ not json"));

        act.Should().Throw<InvalidDataException>().WithMessage("*Invalid JSON*");
    }

    [Fact]
    public void DeclaresTheJsonKind()
    {
        parser.Kind.Should().Be(DocumentKind.Json);
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
}
