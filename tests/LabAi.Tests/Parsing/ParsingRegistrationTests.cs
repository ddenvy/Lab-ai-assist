using LabAi.Domain.Abstractions;
using LabAi.Domain.Enums;
using LabAi.Infrastructure.Parsing;
using Microsoft.Extensions.DependencyInjection;

namespace LabAi.Tests.Parsing;

public sealed class ParsingRegistrationTests
{
    [Fact]
    public void RegistersExactlyOneParserPerDocumentKind()
    {
        var services = new ServiceCollection().AddLabAiParsing();

        using var provider = services.BuildServiceProvider();
        var parsers = provider.GetServices<IDocumentParser>().ToList();

        parsers.Select(p => p.Kind).Should().OnlyHaveUniqueItems();
        Enum.GetValues<DocumentKind>().Should().OnlyContain(kind =>
            parsers.Select(p => p.Kind).Contains(kind),
            "every document kind the ingest pipeline can encounter must have a parser");
    }
}
