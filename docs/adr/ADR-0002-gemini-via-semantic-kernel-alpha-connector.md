# ADR-0002: Gemini through the Semantic Kernel alpha connector, behind our own ports

- **Status:** Accepted
- **Date:** 2026-09-10
- **Milestone:** M1 part 3
- **Deciders:** Danil Lobanov

## Context

The assistant needs two capabilities from an LLM provider: chat completion for grounded generation,
and text embeddings for indexing and query. Google Gemini is the chosen provider because it matches
production experience (the Valletta AI layer used Semantic Kernel + Gemini) and an API key is already
available locally.

The available .NET route is `Microsoft.SemanticKernel.Connectors.Google`. Two facts about it were
verified against NuGet and the assembly itself rather than assumed:

1. **It has no stable release.** The entire series is `1.76.0-alpha … 1.80.1-alpha`, while
   `Microsoft.SemanticKernel` core is stable at 1.80.1. The alpha status is the connector's steady
   state, not a transient pre-release.
2. **Its API has already churned once, mid-flight.** In 1.80.1-alpha,
   `AddGoogleAIEmbeddingGeneration` and `GoogleAITextEmbeddingGenerationService` carry
   `[Obsolete("Use AddGoogleAIEmbeddingGenerator instead.")]`. The replacement returns
   `Microsoft.Extensions.AI.IEmbeddingGenerator<string, Embedding<float>>` — a different abstraction
   from a different package — rather than SK's `ITextEmbeddingGenerationService`.

A reflection probe of the 1.80.1-alpha assembly (net10.0 TFM group) recorded the live surface:

| Member | Signature | Status |
|---|---|---|
| `AddGoogleAIGeminiChatCompletion` | `(IServiceCollection, string modelId, string apiKey, GoogleAIVersion apiVersion = V1_Beta, string? serviceId = null)` | current |
| `AddGoogleAIEmbeddingGenerator` | `(IServiceCollection, string modelId, string apiKey, GoogleAIVersion apiVersion = V1_Beta, string? serviceId = null, HttpClient httpClient = null, int? dimensions = null)` | current |
| `AddGoogleAIEmbeddingGeneration` | `(IServiceCollection, string modelId, string apiKey, ...)` | **`[Obsolete]`** |
| `AddGoogleAIChatClient` | `(IServiceCollection, string modelId, Client googleClient = null, ...)` | current, but takes a `Google.GenAI.Client` |
| `GoogleAIVersion` | `V1 = 0`, `V1_Beta = 1` | current |
| `GeminiPromptExecutionSettings.Temperature` | `double?` (not `float?`) | current |

The probe also established the failure behaviour that shapes the degraded-mode design:

- Registering either extension with an **empty** key **succeeds** — the connector registers a lazy
  factory and validates only on resolution.
- Registering with **null** throws `ArgumentNullException` immediately.
- Resolving with an empty key throws `ArgumentException: The value cannot be an empty string or
  composed entirely of whitespace. (Parameter 'apiKey')`.

Constraints from `AGENT.md`: Domain and Application must stay framework-free (2.1); sensitive values
must never reach logs (3.2); the offline test suite must run in CI with no credentials (6.1).

## Decision

### 1. Use the alpha connector, pinned exactly, through two adapter files

`Microsoft.SemanticKernel` `1.80.1` and `Microsoft.SemanticKernel.Connectors.Google` `1.80.1-alpha`
are pinned with exact versions — no floating ranges. Only two files in the solution may name a
Semantic Kernel or `Microsoft.Extensions.AI` type:

- `src/LabAi.Infrastructure/Ai/GeminiChatClient.cs` → `IChatCompletionService`
- `src/LabAi.Infrastructure/Ai/GeminiEmbeddingService.cs` → `IEmbeddingGenerator<string, Embedding<float>>`

Everything above Infrastructure consumes our own ports, defined in `LabAi.Domain/Abstractions`:

```csharp
public interface IGroundedChatClient
{
    Task<GroundedChatResult> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}

public interface IEmbeddingService
{
    string ModelId { get; }
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
```

### 2. `AddGoogleAIGeminiChatCompletion`, not the newer `AddGoogleAIChatClient`

`AddGoogleAIChatClient` is the direction the connector is moving (it mirrors the embedding shift to
`Microsoft.Extensions.AI`), but its second parameter is a `Google.GenAI.Client`. Constructing one
would mean naming a type from `Google.GenAI` in our code, and that package must not be referenced
directly: the connector is compiled against `0.11.0` while `1.21.0` exists upstream, so an explicit
reference would make NuGet resolve an untested major.

`AddGoogleAIGeminiChatCompletion(modelId, apiKey)` keeps `Google.GenAI` entirely transitive. The
asymmetry — chat via SK's `IChatCompletionService`, embeddings via `Microsoft.Extensions.AI`'s
`IEmbeddingGenerator` — is accepted knowingly. Both sit behind our ports, so the asymmetry is
invisible above Infrastructure and migrating chat to `IChatClient` later touches one file.

### 3. `GoogleAIVersion.V1_Beta` is passed explicitly

It is already the default. It is stated anyway because an alpha package changing its own default
would silently change the meaning of every stored embedding, and this project's whole premise is that
stored evidence stays interpretable. A model or API-version change must be a reviewed code change
followed by a re-ingest, never a side effect of a dependency bump.

### 4. No per-call embedding model override

`EmbedAsync` passes no `EmbeddingGenerationOptions`. The generator is registered once with the
configured model id. Overriding the model per call is precisely how a store ends up holding two
incomparable vector spaces; `IEmbeddingService.ModelId` is persisted on every chunk and the vector
store refuses to search a store containing more than one `(model, dimension)` pair (see ADR-0001).

### 5. Degraded mode instead of fail-fast

The host starts without `GEMINI_API_KEY`. This is a deliberate deviation from the original plan,
which called for fail-fast, and the reason is the probe result above: because registration with an
empty key succeeds, the connector can be registered unconditionally and the failure deferred.

- `Program.cs` logs a `Warning` naming the missing variable and the fix.
- Both adapters check `GeminiOptions.IsConfigured` and throw `GeminiNotConfiguredException` —
  an `InvalidOperationException` subclass — before resolving anything from the connector.
- `GET /health` reports `status: degraded` and `geminiKeyPresent: false`.
- `POST /api/chat` maps `GeminiNotConfiguredException` to `503` with the actionable message.

The alternative — fail-fast at startup — would make the golden-set and `WebApplicationFactory` E2E
suites impossible to host at all without credentials, which contradicts the requirement that
`dotnet test` passes offline.

### 6. No fallback to a second embedding model

The plan sketched a fallback to `text-embedding-004` when `gemini-embedding-001` is not found. It is
deliberately **not** implemented. `text-embedding-004` produces 768-dimensional vectors against
`gemini-embedding-001`'s 3072, so a successful fallback would populate the store with a different
vector space than the one the operator believes is in use — converting a clear startup error into a
silent dimension change discovered later by the guard, or worse, a mixed store. Failing loudly is the
correct behaviour; the fix is a configuration change plus a re-ingest.

### 7. The default chat model is pinned, and the plan's choice turned out to be retired

The live suite caught a failure no amount of offline testing could: with a valid key,
`gemini-2.5-flash` — the model the plan specified — answers

```
404 NOT_FOUND: "This model models/gemini-2.5-flash is no longer available to new users.
Please update your code to use models/gemini-3.6-flash ..."
```

on both `/v1/` and `/v1beta/`, while the same key returns 3072-dimensional vectors from
`gemini-embedding-001` without complaint. The model still appears in `GET /v1beta/models`, so the
listing endpoint is not a usable availability check; only `generateContent` is.

Measured against the real service on 2026-09-10:

| Model | `generateContent` with `temperature: 0, topP: 0.95, maxOutputTokens: 2048, candidateCount: 1` |
|---|---|
| `gemini-2.5-flash` | 404 — retired for new keys |
| `gemini-3.6-flash` | 503 `UNAVAILABLE` (high demand) — valid id, but not reliably reachable |
| `gemini-3.5-flash` | 200 |
| `gemini-flash-latest` | 200 |
| `gemini-3.1-flash-lite` | 200 |

The default becomes **`gemini-3.5-flash`**. `gemini-flash-latest` also works but is rejected: it is a
rolling alias, and the audit log stores the configured model id as evidence of what answered. An alias
that silently moves to a newer model would make that record false while still looking correct —
exactly the failure mode `Temperature = 0` exists to prevent. `gemini-3.6-flash` is Google's
recommended successor but was observed returning 503 under load, and a demo that intermittently fails
on the first question is worse than one pinned a generation back.

Model ids therefore live in `.env` / `appsettings.json` / `GeminiOptions` defaults, never in code
paths, and unit tests assert the fallback *mechanism* against `GeminiOptions` rather than a literal
string — a retirement must not break an unrelated test.

## Exit ramp

If the alpha connector breaks hard — a removed extension, an incompatible `Google.GenAI` major forced
through the graph, or a net10.0 TFM group that disappears — both adapters are rewritten directly
against the Gemini REST API:

- `POST https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent`
- `POST https://generativelanguage.googleapis.com/v1beta/models/{model}:embedContent`

Under 100 lines with a shared `HttpClient`, no Semantic Kernel reference at all, and **zero changes**
to Domain, Application, Web or any test that does not name an adapter. This is the payoff for the
port design and the reason the blast radius was reduced to two files in the first place.

## Alternatives considered

| Option | Why not |
|---|---|
| Expose `IChatCompletionService` / `IEmbeddingGenerator` above Infrastructure | Puts an alpha package's types into Domain and Application, breaks AGENT.md 2.1, and forces every churn event to ripple through the whole solution |
| Wait for a stable connector release | There is no evidence one is coming; the alpha series spans 1.76 → 1.80 |
| Direct `Google.GenAI` SDK now | Would work, but abandons Semantic Kernel — the technology the target job descriptions ask for — and the SDK is at `0.x` itself |
| Hand-rolled REST client from day one | Viable (it is the exit ramp), but gives up SK's telemetry, `PromptExecutionSettings` mapping and the production-experience signal for no present benefit |
| OpenAI instead of Gemini | Equally viable technically; Gemini matches prior production work and a key is already available |

## Consequences

**Positive**

- Alpha churn is confined to two files, both covered by tests that name them explicitly.
- The offline golden set can substitute a deterministic `IEmbeddingService` stub, so CI needs no
  credentials and no network.
- `Temperature = 0` and `CandidateCount = 1` are set in one place, which is what makes `PromptHash`
  meaningful as evidence.
- The REST exit ramp can be exercised without touching a single line above Infrastructure.

**Negative**

- The solution depends on an alpha package. `dotnet restore` on a future SDK could surface a break
  with no code change on our side.
- Chat and embeddings sit on two different abstraction generations inside the connector, which is
  mildly confusing when reading the adapters.
- Exact version pinning means security fixes require a deliberate, tested bump rather than arriving
  automatically.

**Obligations created**

- `Live/LiveGeminiTests.cs` is opt-in on `GEMINI_API_KEY` and must never block CI; it is the only
  thing that detects connector breakage against the real service.
- Any change to `EmbeddingModelId` or `GoogleAIVersion` requires a full re-ingest, and the dimension
  guard must keep refusing mixed stores.
- Logging in both adapters stays limited to sizes, counts, dimensions and model ids. Prompt text,
  answer text and embedded text must never be logged at any level (AGENT.md 3.2).
- The 503 observed on `gemini-3.6-flash` makes the planned retry policy (three attempts, honour
  `Retry-After`, only on 429/503) a measured requirement rather than a theoretical one. Neither
  adapter implements it yet; today a 503 surfaces raw as `HttpOperationException`.
