using LabAi.Domain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LabAi.Infrastructure.Parsing;

public static class ParsingServiceCollectionExtensions
{
    /// <summary>
    /// Registers one <see cref="IDocumentParser"/> per document kind. Parsers are stateless, so
    /// singletons are safe; the ingest pipeline resolves them all and selects by kind.
    /// </summary>
    public static IServiceCollection AddLabAiParsing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IDocumentParser, MarkdownDocumentParser>();
        services.AddSingleton<IDocumentParser, InstrumentCsvParser>();
        services.AddSingleton<IDocumentParser, PdfPigPdfParser>();
        services.AddSingleton<IDocumentParser, JsonDocumentParser>();
        return services;
    }
}
