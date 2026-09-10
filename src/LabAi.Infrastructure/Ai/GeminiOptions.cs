namespace LabAi.Infrastructure.Ai;

/// <summary>
/// Resolved Gemini settings. Built in the composition root from environment variables first
/// (loaded from <c>.env</c>) with <c>appsettings.json</c> as fallback, so no secret ever has to
/// live in a committed file.
/// </summary>
public sealed class GeminiOptions
{
    /// <summary>API key. Empty means the host runs in a degraded, key-less mode (see <see cref="IsConfigured"/>).</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Chat model used for grounded generation. Deliberately a pinned name rather than a rolling
    /// alias such as <c>gemini-flash-latest</c>: this string is what the audit log records as
    /// evidence of which model answered, so a silent swap behind an alias would falsify it.
    /// </summary>
    public string ChatModelId { get; init; } = "gemini-3.5-flash";

    /// <summary>
    /// Embedding model used for indexing and queries. Must stay constant for the lifetime of a
    /// populated database: the vector store refuses to search a store holding more than one
    /// (model, dimension) pair.
    /// </summary>
    public string EmbeddingModelId { get; init; } = "gemini-embedding-001";

    /// <summary>Whether a usable API key is present.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
