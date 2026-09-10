using LabAi.Domain.Abstractions;
using LabAi.Domain.Enums;
using LabAi.Infrastructure.Chunking;
using Microsoft.Extensions.DependencyInjection;

namespace LabAi.Tests.Chunking;

public sealed class ChunkingRegistrationTests
{
    [Fact]
    public void RegisteredStrategiesCoverEveryKindWithoutOverlap()
    {
        var services = new ServiceCollection().AddLabAiChunking(maxChunkChars: 2000, overlapChars: 200);

        using var provider = services.BuildServiceProvider();
        var strategies = provider.GetServices<IChunkingStrategy>().ToList();

        var supported = strategies.SelectMany(s => s.SupportedKinds).ToList();
        supported.Should().OnlyHaveUniqueItems("a document kind must resolve to exactly one chunking strategy");
        Enum.GetValues<DocumentKind>().Should().OnlyContain(kind =>
            supported.Contains(kind),
            "every document kind the ingest pipeline can encounter must have a chunking strategy");
    }
}
