using LabAi.Infrastructure.Ai;

namespace LabAi.Web.Infrastructure;

/// <summary>
/// Resolves <see cref="GeminiOptions"/> in the composition root: environment variables (populated
/// from <c>.env</c> by DotNetEnv before the host is built) win over <c>appsettings.json</c>.
/// The env names are SCREAMING_SNAKE_CASE, which ASP.NET Core configuration does not map onto
/// nested keys on its own, so the mapping is written out here and covered by tests.
/// </summary>
public static class GeminiOptionsFactory
{
    /// <summary>Configuration section used as the fallback source.</summary>
    public const string SectionName = "Gemini";

    public static GeminiOptions Create(IConfiguration configuration) =>
        Create(configuration, Environment.GetEnvironmentVariable);

    /// <param name="readEnvironment">
    /// Environment lookup, injectable so the precedence rule can be tested without mutating
    /// process-wide state that parallel test classes would race on.
    /// </param>
    public static GeminiOptions Create(IConfiguration configuration, Func<string, string?> readEnvironment)
    {
        var section = configuration.GetSection(SectionName);
        var defaults = new GeminiOptions();

        return new GeminiOptions
        {
            ApiKey = First(
                readEnvironment("GEMINI_API_KEY"),
                section[nameof(GeminiOptions.ApiKey)],
                defaults.ApiKey),

            ChatModelId = First(
                readEnvironment("GEMINI_MODEL"),
                section[nameof(GeminiOptions.ChatModelId)],
                defaults.ChatModelId),

            EmbeddingModelId = First(
                readEnvironment("GEMINI_EMBEDDING_MODEL"),
                section[nameof(GeminiOptions.EmbeddingModelId)],
                defaults.EmbeddingModelId),
        };
    }

    private static string First(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate;
        }

        return string.Empty;
    }
}
