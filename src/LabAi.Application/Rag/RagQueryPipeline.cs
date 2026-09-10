using LabAi.Application.Security;
using LabAi.Application.Vectors;
using LabAi.Domain.Abstractions;
using LabAi.Domain.Enums;
using LabAi.Domain.ValueObjects;

namespace LabAi.Application.Rag;

/// <summary>
/// Orchestrates the full RAG flow: embed question → retrieve top-K chunks → threshold gate →
/// compose prompt → call LLM → detect refusal → extract rationale. The audit entry is written
/// BEFORE the answer is returned (fail-closed: if audit fails, no answer is delivered).
/// </summary>
public sealed class RagQueryPipeline : IRagQueryService
{
    private const string UpstreamFailureAnswer =
        "The language model is temporarily unavailable. Your question was recorded in the audit journal; please try again shortly.";

    private readonly IEmbeddingService embeddings;
    private readonly IVectorStore vectorStore;
    private readonly IGroundedChatClient chat;
    private readonly IAiAuditTrail audit;
    private readonly IDocumentRepository documentRepository;
    private readonly IPiiMasker piiMasker;
    private readonly int topK;
    private readonly double minScore;

    public RagQueryPipeline(
        IEmbeddingService embeddings,
        IVectorStore vectorStore,
        IGroundedChatClient chat,
        IAiAuditTrail audit,
        IDocumentRepository documentRepository,
        IPiiMasker piiMasker,
        int topK,
        double minScore)
    {
        ArgumentNullException.ThrowIfNull(embeddings);
        ArgumentNullException.ThrowIfNull(vectorStore);
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(documentRepository);
        ArgumentNullException.ThrowIfNull(piiMasker);
        ArgumentOutOfRangeException.ThrowIfLessThan(topK, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(minScore, 0.0);

        this.embeddings = embeddings;
        this.vectorStore = vectorStore;
        this.chat = chat;
        this.audit = audit;
        this.documentRepository = documentRepository;
        this.piiMasker = piiMasker;
        this.topK = topK;
        this.minScore = minScore;
    }

    public async Task<RagAnswer> AskAsync(
        string question,
        long actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        // 1. Mask PII in the question before any processing.
        var maskedQuestion = piiMasker.Mask(question);

        // 2. Embed the masked question (PII already removed).
        var queryEmbeddings = await embeddings.EmbedAsync(new[] { maskedQuestion }, cancellationToken);
        var queryEmbedding = queryEmbeddings[0];

        // 2. Retrieve top-K chunks from the vector store snapshot.
        var snapshot = vectorStore.Snapshot;
        var hitsBuffer = new SearchHit[topK];
        var hitCount = BruteForceSearch.TopK(snapshot, queryEmbedding, topK, (float)minScore, hitsBuffer.AsSpan(0, topK));

        // Resolve chunk metadata from repository for citation details.
        var chunkIds = new List<long>(hitCount);
        for (var i = 0; i < hitCount; i++)
            chunkIds.Add(hitsBuffer[i].ChunkId);

        var chunksById = await documentRepository.GetChunksByIdsAsync(chunkIds, cancellationToken);

        var retrievedChunks = new List<RetrievedChunk>(hitCount);
        for (var i = 0; i < hitCount; i++)
        {
            var hit = hitsBuffer[i];
            if (!chunksById.TryGetValue(hit.ChunkId, out var chunk))
                continue; // Chunk was deleted between indexing and retrieval — skip silently.

            // Resolve document title/version from the chunk's navigation property.
            var docTitle = chunk.Document?.Title ?? $"Document-{hit.DocumentId}";
            var docVersion = chunk.Document?.Version ?? 1;
            var sectionPath = chunk.SectionPath ?? "N/A";

            retrievedChunks.Add(new RetrievedChunk(
                ChunkId: hit.ChunkId,
                DocumentId: hit.DocumentId,
                Title: docTitle,
                Version: docVersion,
                SectionPath: sectionPath,
                Text: chunk.Text,
                Score: hit.Score));
        }

        // 3. Threshold gate: if zero hits, refuse without calling the LLM.
        var hadRetrievedChunks = retrievedChunks.Count > 0;

        string? answer = null;
        string? rationale = null;
        RefusalStage refusalStage;
        string promptHash = string.Empty;
        GroundedChatResult? chatResult = null;

        if (!hadRetrievedChunks)
        {
            // Threshold gate — skip LLM entirely.
            answer = "Not found in sources.";
            refusalStage = RefusalStage.ThresholdGate;
        }
        else
        {
            // 4. Compose the grounded prompt.
            var (userPrompt, citations) = PromptComposer.Compose(question, retrievedChunks);

            // 5. Call the LLM. A failed call (e.g. a transient 503 that survived the adapter's retries)
            //    must not abort the request before step 8: the query still has to land in the journal.
            //    Cancellation is rethrown so the UI's "[Request cancelled]" branch keeps working.
            try
            {
                chatResult = await chat.CompleteAsync(
                    systemPrompt: PromptComposer.SystemPrompt,
                    userPrompt: userPrompt,
                    cancellationToken);

                answer = chatResult.Text;
                var modelId = chatResult.ModelId;

                // 6. Compute prompt hash for audit evidence.
                promptHash = PromptHasher.Compute(PromptComposer.SystemPrompt, userPrompt, modelId);

                // 7. Detect refusal and extract rationale.
                refusalStage = RefusalDetector.DetermineStage(hadRetrievedChunks, answer);
                rationale = RefusalDetector.ExtractRationale(answer);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                answer = UpstreamFailureAnswer;
                refusalStage = RefusalStage.UpstreamFailure;
            }
        }

        // 8. Write audit entry BEFORE returning the answer (fail-closed).
        var auditEntryId = await audit.AppendAsync(new AiAuditEntryDraft(
            TimestampUtc: DateTime.UtcNow,
            ActorUserId: actorUserId,
            CorrelationId: correlationId,
            QuestionMasked: maskedQuestion,
            RetrievedChunkIds: retrievedChunks.Select(c => c.ChunkId).ToArray(),
            TopScore: retrievedChunks.Count > 0 ? retrievedChunks[0].Score : 0,
            Model: hadRetrievedChunks ? (chatResult?.ModelId ?? "unknown") : "N/A",
            PromptHash: promptHash,
            AnsweredFromContext: refusalStage == RefusalStage.None,
            RefusalStage: refusalStage,
            AnswerMasked: piiMasker.Mask(answer),
            Rationale: rationale),
            cancellationToken);

        return new RagAnswer(
            Answer: answer ?? string.Empty,
            AnsweredFromContext: refusalStage == RefusalStage.None,
            RefusalStage: refusalStage,
            Citations: hadRetrievedChunks
                ? (IReadOnlyList<CitedChunk>)retrievedChunks.Select((c, i) => new CitedChunk(i + 1, c.ChunkId, c.DocumentId, c.Score)).ToList()
                : Array.Empty<CitedChunk>(),
            Rationale: rationale,
            AuditEntryId: auditEntryId,
            CorrelationId: correlationId);
    }
}
