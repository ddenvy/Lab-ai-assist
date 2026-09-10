namespace LabAi.Infrastructure.Ai;

/// <summary>
/// Thrown when an AI operation is attempted while no API key is configured.
/// </summary>
/// <remarks>
/// The host deliberately starts without a key so the offline golden-set and E2E suites can run, which
/// moves the failure from startup to first use. This type exists so that failure is distinguishable
/// from a genuine upstream fault: endpoints map it to 503 with an actionable message instead of a
/// generic 500, and it never carries the key or any part of it.
/// </remarks>
public sealed class GeminiNotConfiguredException : InvalidOperationException
{
    public GeminiNotConfiguredException()
        : base("Gemini is not configured: GEMINI_API_KEY is missing. " +
               "Copy .env.example to .env, set the key, then restart the host.")
    {
    }
}
