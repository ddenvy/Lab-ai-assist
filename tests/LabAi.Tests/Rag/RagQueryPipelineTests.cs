using System.Net;
using LabAi.Application.Rag;
using LabAi.Domain.Abstractions;
using LabAi.Domain.Entities;
using LabAi.Domain.Enums;
using LabAi.Domain.ValueObjects;
using NSubstitute;

namespace LabAi.Tests.Rag;

/// <summary>
/// Locks the fail-closed guarantee that a failed upstream model call (e.g. a transient HTTP 503 that
/// survived the adapter's retries) still produces a journaled audit row and a graceful refusal, rather
/// than aborting the request and silently dropping the query from the append-only trail.
/// </summary>
public sealed class RagQueryPipelineTests
{
    [Fact]
    public async Task JournalsAnUpstreamFailure_AsARefusal_InsteadOfDroppingTheQuery()
    {
        AiAuditEntryDraft? captured = null;
        var pipeline = Pipeline(chat: FailingChat(), capture: d => captured = d);

        var result = await pipeline.AskAsync("what is the flow rate?", actorUserId: 1, correlationId: "corr");

        captured.Should().NotBeNull("the failed query must still land in the append-only journal");
        captured!.RefusalStage.Should().Be(RefusalStage.UpstreamFailure);
        result.RefusalStage.Should().Be(RefusalStage.UpstreamFailure);
        result.AnsweredFromContext.Should().BeFalse();
        result.Answer.Should().Contain("temporarily unavailable");
        result.AuditEntryId.Should().Be(7);
    }

    [Fact]
    public async Task PropagatesCancellation_SoTheUiCancelBranchKeepsWorking()
    {
        using var cts = new CancellationTokenSource();
        var pipeline = Pipeline(chat: CancellingChat(cts), capture: _ => { });

        var act = () => pipeline.AskAsync("q", 1, "corr", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static RagQueryPipeline Pipeline(IGroundedChatClient chat, Action<AiAuditEntryDraft> capture)
    {
        var embeddings = Substitute.For<IEmbeddingService>();
        embeddings
            .EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new[] { 1f, 0f, 0f } });

        var vectorStore = Substitute.For<IVectorStore>();
        vectorStore.Snapshot.Returns(new SearchIndex([1], [1], [1f, 0f, 0f], dimension: 3));

        var chunk = new Chunk
        {
            Id = 1,
            DocumentId = 1,
            Document = new SourceDocument { Id = 1, Title = "Test SOP", Version = 1 },
            Text = "HPLC_flow_rate=1.0 mL/min",
            SectionPath = "Rows",
        };
        var documents = Substitute.For<IDocumentRepository>();
        documents
            .GetChunksByIdsAsync(Arg.Any<IReadOnlyList<long>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<long, Chunk> { [1] = chunk });

        var audit = Substitute.For<IAiAuditTrail>();
        audit
            .AppendAsync(Arg.Do<AiAuditEntryDraft>(capture), Arg.Any<CancellationToken>())
            .Returns(7);

        var piiMasker = Substitute.For<IPiiMasker>();
        piiMasker.Mask(Arg.Any<string>()).Returns(ci => ci.Arg<string>());

        return new RagQueryPipeline(
            embeddings,
            vectorStore,
            chat,
            audit,
            documents,
            piiMasker,
            topK: 5,
            minScore: 0.35);
    }

    private static IGroundedChatClient FailingChat()
    {
        var chat = Substitute.For<IGroundedChatClient>();
        chat
            .CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<GroundedChatResult>>(
                _ => Task.FromException<GroundedChatResult>(
                    new HttpRequestException("upstream", null, HttpStatusCode.ServiceUnavailable)));
        return chat;
    }

    private static IGroundedChatClient CancellingChat(CancellationTokenSource cts)
    {
        var chat = Substitute.For<IGroundedChatClient>();
        chat
            .CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<GroundedChatResult>>(_ =>
            {
                cts.Cancel();
                return Task.FromCanceled<GroundedChatResult>(cts.Token);
            });
        return chat;
    }
}
