using System.Text.Json;
using LabAi.Domain.Abstractions;
using LabAi.Domain.Enums;
using LabAi.Domain.ValueObjects;

namespace LabAi.Infrastructure.Parsing;

/// <summary>
/// Flattens JSON into <c>"path: value"</c> lines that read like the flat notation a lab analyst
/// would scan by eye (<c>runs[2].owner</c>), because that is the text the embedding will see. A
/// root array — the shape of an instrument log — becomes one section per element; any other root
/// becomes a single section. Null and empty containers produce nothing: they carry no retrievable
/// information and would only dilute neighbouring chunks.
/// </summary>
public sealed class JsonDocumentParser : IDocumentParser
{
    public DocumentKind Kind => DocumentKind.Json;

    public ParsedDocument Parse(byte[] content)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Invalid JSON at position {exception.BytePositionInLine}: {exception.Message}", exception);
        }

        using (document)
        {
            var sections = new List<DocumentSection>();

            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    AppendSection(sections, $"[{index}]", element);
                    index++;
                }
            }
            else
            {
                AppendSection(sections, "Json", document.RootElement);
            }

            return new ParsedDocument(sections);
        }
    }

    private static void AppendSection(List<DocumentSection> sections, string path, JsonElement element)
    {
        var lines = new List<string>();
        Flatten(lines, path, element);
        if (lines.Count == 0)
            return;

        sections.Add(new DocumentSection(
            SectionPath: path,
            Text: string.Join('\n', lines)));
    }

    private static void Flatten(List<string> lines, string path, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    Flatten(lines, $"{path}.{property.Name}", property.Value);
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Flatten(lines, $"{path}[{index}]", item);
                    index++;
                }

                break;

            case JsonValueKind.String:
                // Multi-line strings would corrupt the one-fact-per-line format the chunker relies on.
                lines.Add($"{path}: {element.GetString()!.ReplaceLineEndings(" ")}");
                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                lines.Add($"{path}: {element.GetRawText()}");
                break;
        }
    }
}
