using System.Security.Cryptography;
using System.Text;
using LabAi.Application.Vectors;
using LabAi.Domain.Abstractions;
using LabAi.Domain.Entities;
using LabAi.Domain.Enums;
using LabAi.Domain.ValueObjects;

namespace LabAi.Application.Ingest;

/// <summary>
/// Turns one raw file into a stored document with embedded chunks: hash → parse → chunk → embed in
/// batches → L2-normalize → persist in a single transaction. Idempotent by content hash — identical
/// bytes return the existing document and never reach the embedding model. Changed content under a
/// known source path supersedes the previous version in the same transaction that inserts the new
/// one. PII masking is inserted before the first embedding call in Milestone 5; until then this
/// flow must not be pointed at real sensitive corpora.
/// </summary>
public sealed class IngestPipeline : IDocumentIngestService
{
    private static readonly string ContextHeaderSeparator = " — ";

    private readonly IReadOnlyList<IDocumentParser> parsers;
    private readonly IReadOnlyList<IChunkingStrategy> strategies;
    private readonly IEmbeddingService embeddings;
    private readonly IDocumentRepository repository;
    private readonly int embeddingBatchSize;

    public IngestPipeline(
        IEnumerable<IDocumentParser> parsers,
        IEnumerable<IChunkingStrategy> strategies,
        IEmbeddingService embeddings,
        IDocumentRepository repository,
        int embeddingBatchSize)
    {
        ArgumentNullException.ThrowIfNull(parsers);
        ArgumentNullException.ThrowIfNull(strategies);
        ArgumentNullException.ThrowIfNull(embeddings);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentOutOfRangeException.ThrowIfLessThan(embeddingBatchSize, 1);

        this.parsers = parsers.ToArray();
        this.strategies = strategies.ToArray();
        this.embeddings = embeddings;
        this.repository = repository;
        this.embeddingBatchSize = embeddingBatchSize;
    }

    public async Task<IngestResult> IngestAsync(IngestRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Content);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Title);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.Version, 1);

        // Hash before anything else: it is both the idempotency key and the cheapest gate — a
        // duplicate never pays for parsing, let alone for embedding.
        var contentHash = Convert.ToHexStringLower(SHA256.HashData(request.Content));
        var duplicate = await repository.FindActiveByContentHashAsync(contentHash, cancellationToken);
        if (duplicate is not null)
            return new IngestResult(duplicate.Id, IngestOutcome.Duplicate, ChunksCreated: 0, SupersededDocumentId: null);

        var parsed = ParserFor(request.Kind).Parse(request.Content);
        var drafts = StrategyFor(request.Kind).Chunk(parsed);
        var ingestedAtUtc = DateTime.UtcNow;

        if (drafts.Count == 0)
        {
            var emptyDocument = BuildDocument(request, contentHash, ingestedAtUtc);
            var emptyDocumentId = await repository.AddAsync(emptyDocument, _ => [], null, cancellationToken);
            return new IngestResult(emptyDocumentId, IngestOutcome.Created, ChunksCreated: 0, SupersededDocumentId: null);
        }

        var vectors = await EmbedAsync(drafts, request, cancellationToken);
        var dimension = RequireSingleDimension(vectors);

        // Looked up late on purpose: the supersede decision is made against the state that is
        // current when the new version is about to be committed.
        var superseded = await repository.FindActiveBySourcePathAsync(request.SourcePath, cancellationToken);

        var document = BuildDocument(request, contentHash, ingestedAtUtc);
        IReadOnlyList<Chunk> BuildChunks(long documentId)
        {
            var chunks = new List<Chunk>(drafts.Count);
            for (var i = 0; i < drafts.Count; i++)
            {
                var draft = drafts[i];
                VectorMath.NormalizeInPlace(vectors[i]);
                chunks.Add(new Chunk
                {
                    DocumentId = documentId,
                    ChunkIndex = i,
                    Text = draft.Text,
                    SectionPath = draft.SectionPath,
                    PageStart = draft.PageStart,
                    PageEnd = draft.PageEnd,
                    LineStart = draft.LineStart,
                    LineEnd = draft.LineEnd,
                    ContentHash = Sha256Hex(draft.Text),
                    EmbeddingModelId = embeddings.ModelId,
                    EmbeddingDimension = dimension,
                    Embedding = VectorMath.ToFloat32Blob(vectors[i]),
                    CreatedAtUtc = ingestedAtUtc,
                });
            }

            return chunks;
        }

        var documentId = await repository.AddAsync(document, BuildChunks, superseded?.Id, cancellationToken);
        return new IngestResult(documentId, IngestOutcome.Created, drafts.Count, superseded?.Id);
    }

    private IDocumentParser ParserFor(DocumentKind kind)
    {
        return parsers.FirstOrDefault(p => p.Kind == kind)
            ?? throw new InvalidOperationException($"No parser is registered for document kind {kind}.");
    }

    private IChunkingStrategy StrategyFor(DocumentKind kind)
    {
        return strategies.FirstOrDefault(s => s.SupportedKinds.Contains(kind))
            ?? throw new InvalidOperationException($"No chunking strategy is registered for document kind {kind}.");
    }

    private async Task<List<float[]>> EmbedAsync(IReadOnlyList<ChunkDraft> drafts, IngestRequest request, CancellationToken cancellationToken)
    {
        var vectors = new List<float[]>(drafts.Count);
        for (var offset = 0; offset < drafts.Count; offset += embeddingBatchSize)
        {
            var count = Math.Min(embeddingBatchSize, drafts.Count - offset);
            var batch = new List<string>(count);
            for (var i = offset; i < offset + count; i++)
            {
                // The context header is prepended only for the model; Chunk.Text stays bare, and
                // the prompt composer reconstructs the same header deterministically at query time.
                batch.Add($"{request.Title} (v{request.Version}){ContextHeaderSeparator}{drafts[i].SectionPath}\n{drafts[i].Text}");
            }

            var embedded = await embeddings.EmbedAsync(batch, cancellationToken);
            if (embedded.Count != count)
            {
                throw new InvalidOperationException(
                    $"Embedding service returned {embedded.Count} vectors for a batch of {count}.");
            }

            vectors.AddRange(embedded);
        }

        return vectors;
    }

    private static int RequireSingleDimension(IReadOnlyList<float[]> vectors)
    {
        var dimension = vectors[0].Length;
        if (dimension == 0)
            throw new InvalidOperationException("Embedding service returned an empty vector.");

        foreach (var vector in vectors)
        {
            if (vector.Length != dimension)
            {
                throw new InvalidOperationException(
                    $"Embedding service returned inconsistent dimensions ({dimension} and {vector.Length}). " +
                    "Vectors from different models or settings must never be mixed in one document.");
            }
        }

        return dimension;
    }

    private static SourceDocument BuildDocument(IngestRequest request, string contentHash, DateTime ingestedAtUtc)
    {
        return new SourceDocument
        {
            SourcePath = request.SourcePath,
            Title = request.Title,
            Version = request.Version,
            Kind = request.Kind,
            ContentHash = contentHash,
            Status = DocumentStatus.Active,
            SupersededByDocumentId = null,
            IngestedAtUtc = ingestedAtUtc,
            IngestedByUserId = request.IngestedByUserId,
        };
    }

    private static string Sha256Hex(string text)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
