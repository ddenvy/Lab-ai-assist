using System.Threading;
using LabAi.Application.Vectors;
using LabAi.Domain.Abstractions;
using LabAi.Domain.Entities;
using LabAi.Domain.Enums;
using LabAi.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace LabAi.Infrastructure.Persistence;

/// <summary>
/// EF-backed vector store. Rebuilds an immutable <see cref="SearchIndex"/> snapshot from the
/// database and publishes it atomically via <c>Volatile.Write</c>. Only Active documents are
/// included — superseded vectors never pollute the hot loop. A dimension guard refuses to build
/// a snapshot when the store holds embeddings from multiple model/dimension pairs.
/// </summary>
public sealed class EfVectorStore(IDbContextFactory<LabAiDbContext> contextFactory) : IVectorStore
{
    private SearchIndex _snapshot = SearchIndex.Empty;

    public SearchIndex Snapshot => Volatile.Read(ref _snapshot);

    public async Task RebuildAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Dimension guard: more than one (model, dim) pair → refuse to search across incompatible spaces.
        var pairs = await dbContext.Chunks
            .AsNoTracking()
            .Select(c => new { c.EmbeddingModelId, c.EmbeddingDimension })
            .Distinct()
            .ToListAsync(cancellationToken);

        if (pairs.Count > 1)
        {
            throw new InvalidOperationException(
                $"Vector store contains embeddings from {pairs.Count} distinct model/dimension pairs. " +
                "Vectors from different models live in different spaces and must never be mixed. " +
                "Re-ingest all documents with a single embedding model before searching.");
        }

        // Build snapshot from Active-only chunks. Explicit subquery because Chunk has no Document navigation.
        var activeDocIds = await dbContext.SourceDocuments
            .AsNoTracking()
            .Where(d => d.Status == DocumentStatus.Active)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        var rows = await dbContext.Chunks
            .AsNoTracking()
            .Where(c => activeDocIds.Contains(c.DocumentId))
            .OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.DocumentId, c.Embedding, c.EmbeddingDimension })
            .ToListAsync(cancellationToken);

        var count = rows.Count;
        var dimension = count == 0 ? 0 : rows[0].EmbeddingDimension;

        var chunkIds = new long[count];
        var documentIds = new long[count];
        var vectors = new float[count * dimension];

        for (var i = 0; i < count; i++)
        {
            chunkIds[i] = rows[i].Id;
            documentIds[i] = rows[i].DocumentId;

            var decoded = VectorMath.FromFloat32Blob(rows[i].Embedding);
            if (decoded.Length != dimension)
            {
                throw new InvalidOperationException(
                    $"Chunk {chunkIds[i]} has {decoded.Length} floats but the declared dimension is {dimension}.");
            }

            decoded.CopyTo(vectors, i * dimension);
        }

        var snapshot = new SearchIndex(chunkIds, documentIds, vectors, dimension);
        Volatile.Write(ref _snapshot, snapshot);
    }
}
