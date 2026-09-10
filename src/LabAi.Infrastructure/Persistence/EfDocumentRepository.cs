using LabAi.Domain.Abstractions;
using LabAi.Domain.Entities;
using LabAi.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LabAi.Infrastructure.Persistence;

/// <summary>
/// EF-backed document store. The only place that knows the new-version insert and the supersede of
/// the previous version share one transaction.
/// </summary>
public sealed class EfDocumentRepository(LabAiDbContext dbContext) : IDocumentRepository
{
    public Task<SourceDocument?> FindActiveByContentHashAsync(string contentHash, CancellationToken cancellationToken = default)
    {
        return dbContext.SourceDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                d => d.Status == DocumentStatus.Active && d.ContentHash == contentHash,
                cancellationToken);
    }

    public Task<SourceDocument?> FindActiveBySourcePathAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        return dbContext.SourceDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                d => d.Status == DocumentStatus.Active && d.SourcePath == sourcePath,
                cancellationToken);
    }

    public async Task<long> AddAsync(
        SourceDocument document,
        Func<long, IReadOnlyList<Chunk>> buildChunks,
        long? supersededDocumentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(buildChunks);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.SourceDocuments.Add(document);
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.Chunks.AddRange(buildChunks(document.Id));

        if (supersededDocumentId is { } previousId)
        {
            var previous = await dbContext.SourceDocuments.SingleAsync(d => d.Id == previousId, cancellationToken);
            previous.Status = DocumentStatus.Superseded;
            previous.SupersededByDocumentId = document.Id;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return document.Id;
    }
}
