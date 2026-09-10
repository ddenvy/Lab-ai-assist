namespace LabAi.Domain.ValueObjects;

/// <summary>
/// One retrieved neighbour: the chunk that matched, the document it belongs to (the UI resolves
/// title/version from it), and the cosine similarity that ranked it.
/// </summary>
public readonly record struct SearchHit(long ChunkId, long DocumentId, float Score);
