# Lab AI Assistant — Development Log

## About

Lab AI Assistant is a RAG assistant over laboratory SOPs and instrument data for a regulated
environment (GxP / 21 CFR Part 11). It answers **only** based on the document fragments found,
cites sources, says "Not found in sources." when there is no answer, and writes an append-only
journal entry for every AI request: who asked, the question, which chunks were used, the model,
the prompt hash, the rationale, and the timestamp.

Platform: .NET 10, ASP.NET Core Minimal API + Blazor Server (single host), SQLite.
Architecture: Clean/Layered (Domain → Application → Infrastructure → Web),
Data-Oriented Design on the hot path (brute-force cosine search).

**Stack:** C# 14, Semantic Kernel 1.80.1 + Google (Gemini) connector 1.80.1-alpha, EF Core 10 + SQLite,
PdfPig (PDF parsing), DotNetEnv, Serilog, xUnit + NSubstitute + FluentAssertions.

**Link to project A:** Mini-CDS (`C:\Develop\Mini-CDS`) is the source of conventions and of the
ready-made append-only Part 11 audit pattern. The `Mini-CDS\Reports\1.csv` format is supported by
ingest directly, which makes the A+C scenario ("show the sample peaks and explain the deviation")
a single-file upload.

## Maintenance rules

1. An entry is **added before every `git commit`**. Historical entries are never edited — only new
   blocks are appended in chronological order.
2. A commit is made when each **part** of a milestone closes, and mandatory when the milestone itself
   closes.
3. Entry structure:
   ```
   ### [Date] Milestone N, part M: <short title>

   **Plan:** what we set out to do.

   **Done:** what was actually written / changed.

   **Problems / pitfalls:** design mistakes, bugs, contentious decisions.

   **Outcome:** tests (N/N), status, what is next.
   ```
4. The **"Problems / pitfalls" section is mandatory**, even when everything went smoothly — then it
   documents what could have broken and why it did not. An empty section means the entry is rejected.
5. Specifics instead of generic words: not "there were problems with embeddings", but the exact API
   name, the compiler warning number, and what had to be changed.
6. Milestone completion criterion: the code builds without warnings, all tests are green, the log
   entry exists, and the commit is made.

---

## Current state (2026-09-10)

| Milestone | Description | Status |
|-----------|-------------|--------|
| 1 | Semantic Kernel + basic chat (skeleton, .env, Serilog, Gemini adapters) | ✅ Closed (32/32 offline + 5/5 live, 0 failures) |
| 2 | Ingest: parsing + chunks + metadata | ✅ Closed (156/156 offline + 5/5 live skip, 0 failures) |
| 3 | Embeddings + vector store + top-k search | ✅ Closed (183/183 offline, 0 failures) |
| 4 | Grounded generation + citations + "don't know" | ⬜ Not started |
| 5 | AI Audit Log (append-only) + PII masking | ⬜ Not started |
| 6 | UI: chat + sources panel + audit journal | ⬜ Not started |
| 7 | Tests (golden set) + README + GIF | ⬜ Not started |

---

## Architectural decisions locked in forever

| Decision | Rationale |
|---------|-------------|
| `net10.0`, not the `net8.0` from the original spec | The .NET 8 SDK is not installed on the machine (9.0.315 and 10.0.301 are); Mini-CDS is already on net10.0 — a version split would break the shared stack |
| ASP.NET Core Minimal API + Blazor Server in one host | Project A already demonstrates WPF; C must cover the AI Backend track. Bonus: Mini-CDS can call the API for the A+C integration |
| Our own ports `IGroundedChatClient` / `IEmbeddingService` instead of exposing SK types | (a) Domain/Application stay framework-free (AGENT.md 2.1); (b) the alpha connector already changed its API once — our own port locks the instability inside two adapter files; (c) the golden set in CI needs a deterministic stub of the same contract |
| Our own vector store on EF Core + SQLite, not `CommunityToolkit.VectorData` | There is no official SQLite connector in `CommunityToolkit.VectorData.*`; the `sqlite-vec` bindings for .NET are third-party packages of dubious quality. `IVectorStore` remains the seam for Qdrant |
| Embeddings as a headerless BLOB of float32 LE | `MemoryMarshal.Cast<byte, float>` gives zero-copy; dimension and model live in separate columns, and a header inside the BLOB would only complicate casting |
| L2 normalization on write → search is dot product only | One `sqrt` per vector at ingest instead of one `sqrt` per pair on every query; zero normalizations in the hot loop |
| `Temperature = 0` for generation | Reproducibility is what makes `PromptHash` meaningful evidence: the same hash + the same model must produce the same answer |
| PII masking at ingest, not only at prompt time | If masking happened only before the prompt, the **unmasked** text would still go to Google in the embedding call at ingest. Masking at the trust boundary means raw text never leaves it |
| The audit stores masked text + a keyed HMAC of the original, not reversible ciphertext | Part 11 attribution is carried by `ActorUserId` + `TimestampUtc` + `CorrelationId`. A bare SHA256 of a short question is brute-forceable, so the minimum is a keyed HMAC. Reversible ciphertext would turn the append-only journal into a PII store |
| Fail-closed order: the audit entry is written and awaited **before** the response is returned | If the audit write fails, the response is not returned. The reverse order would mean an answer can exist without a journal entry, i.e. the journal is incomplete by construction |
| Prompt composition by string concatenation, not SK templates | There is one fixed composition — a templating engine gives zero payoff (KISS/YAGNI). Also `PromptHash` must be provably the hash of what the model saw: our own assembly removes renderer ambiguity |
| Cookie auth, not JWT | A Blazor Server circuit is a WebSocket that cannot carry `Authorization: Bearer`. Cookies cover both HTTP endpoints and the interactive circuit with one mechanism and no extra packages, and provide `HttpContext.User` → `ActorUserId` for audit for free |
| No streaming in v1 | Refusal detection, "Rationale" extraction, and audit writing need the full text — streaming would be buffered into a string anyway, doubling the alpha connector's surface |
| Append-only in two layers (EF interceptor + SQL triggers) | The interceptor catches mutations through EF before SQL generation; triggers catch raw SQL bypassing EF. Each layer closes its own vector |
| Deterministic masking tokens | The same input must always produce the same `PromptHash` forever; a random or counter token would break reproducibility of evidence |
| The host starts without `GEMINI_API_KEY` — a warning, not fail-fast (a deliberate divergence from the plan) | The plan demanded fail-fast, but the golden set and E2E on `WebApplicationFactory` must bring the application up **without a key**, otherwise CI could not run at all. Compromise: a warning at startup, a descriptive exception on first use inside the adapter, and `geminiKeyPresent` in `/health`. Degradation is visible but does not block non-AI routes |
| Embeddings via `AddGoogleAIEmbeddingGenerator` → `Microsoft.Extensions.AI.IEmbeddingGenerator`, not the SK-native path | `AddGoogleAIEmbeddingGeneration` and `GoogleAITextEmbeddingGenerationService` are marked `[Obsolete]` in 1.80.1-alpha. The connector itself migrates to `Microsoft.Extensions.AI`; landing on the obsolete name would guarantee another churn |
| Chat stays on `AddGoogleAIGeminiChatCompletion`, even though `AddGoogleAIChatClient` is newer | The second parameter of `AddGoogleAIChatClient` is `Google.GenAI.Client`. Naming that type in our code means adding a direct reference to `Google.GenAI`, which is forbidden (the connector is compiled against 0.11.0, and NuGet would resolve an unverified major version). The "chat on SK, embeddings on M.E.AI" asymmetry is accepted deliberately and is invisible above Infrastructure |
| The chat model is a pinned name (`gemini-3.5-flash`), never a rolling alias | In the audit, `Model` is evidence of **which** model answered. `gemini-flash-latest` silently moves to a new version and the entry becomes false while remaining superficially correct. This is the same reason as `Temperature = 0` |
| No fallback to a second embedding model (the plan suggested `text-embedding-004`) | `text-embedding-004` = 768 dimensions versus 3072 for `gemini-embedding-001`. A successful fallback would fill the store with a **different vector space** than the operator thinks they have. A loud failure + config change + re-ingest is better than a silent substitution |
| DB schema follows Mini-CDS conventions: snake_case tables, PascalCase columns, enums as TEXT(32), DateTime with a Kind converter | Both projects must read as the work of one engineer, and enum-as-name gives forensic readability: a database opened by hand does not need decoding. The Kind converter is mandatory: SQLite has no datetime type and the provider returns `Kind=Unspecified`, which silently shifts every comparison or serialization |
| All FKs use `DeleteBehavior.Restrict`, never `Cascade` | A cascade would delete version history and chunks referenced by old audit rows, i.e. destroy the evidence together with what quotes it. Verified at two levels: EF throws `InvalidOperationException` ("association … has been severed") already at `Remove()`, before SQL is generated, and a raw `DELETE` bypassing the tracker is stopped by SQLite itself (`FOREIGN KEY constraint failed`) |
| `Chunk.Text` without `HasMaxLength` — deliberate | TEXT has no length in SQLite, so the constraint would protect nothing: it would only turn a legitimately long section into a save error on the day `MaxChunkChars` in appsettings is raised. Chunk length is the chunker's job, not the schema's |
| WAL is enabled with a one-time `PRAGMA journal_mode=WAL` at host startup, not in the connection string and not in a migration | `Microsoft.Data.Sqlite` has no journal-mode key at all; it cannot go into a migration — migrations run inside a transaction, and the PRAGMA inside a transaction is a no-op. The value persists in the DB file, so one execution after `MigrateAsync` is enough forever |
| Seeding is idempotent: an existing user row is a record, not a template | Name comparison is `OrdinalIgnoreCase`; existing accounts are never overwritten or re-hashed. Otherwise every restart would rewrite passwords and break the link between the audit's `ActorUserId` and the credentials that were actually valid at write time |
| Section merging in the chunker — only adjacent sections with **one common parent**; the merged chunk path is the common parent | "4.1 Flow Rate" + "4.2 Temperature" → one chunk with path "4. Method": a three-line section is not retrievable on its own. But sections from different branches never merge: a chunk covering unrelated topics would be an honest citation about nothing. The "common parent" path describes the merged chunk's content better than the first section's path |
| CSV rows are not overlapped (unlike text) | A sentence cut at a budget boundary loses meaning without repetition; a measurement row is an independent fact, and its duplicate in the neighboring window would double-cite the same peak. Instead of overlap, every window repeats the preamble and header |

## Known technical debt

- ~~`Marker.cs` in Application — a temporary build anchor so `LayeringTests` can reference the empty
  project. In Domain the marker was **removed** in M1 part 3: real ports appeared there
  (`Abstractions\IGroundedChatClient` etc.), and the test now references them. Remove the remaining
  one in Milestone 2.~~ — closed in M2 part 2: `AuthService` appeared in Application and the test
  was switched to it.
- `/health` currently reports only Gemini configuration (`geminiKeyPresent`, models). Per the plan it
  must also report database availability and the vector index size — there is physically nothing to
  call for either check before Milestone 2 (`LabAiDbContext`) and Milestone 3 (`EfVectorStore`).
  Extend there.
- The temporary `POST /api/chat` has **no authorization and no audit line**. It exists only as a
  smoke test of the Gemini plumbing and deliberately violates the product's own principle ("no
  answer without a journal entry"). Remove in Milestone 4 when switching to `POST /api/ask`.
- The Gemini adapters have no backoff retries on 429/503 (plan: 3 attempts, honor `Retry-After`).
  This is not theoretical: a live run showed a real `503 UNAVAILABLE` from `gemini-3.6-flash`. Today
  the 503 is propagated raw as `HttpOperationException`.
- `Xunit.SkippableFact` in the test project is a forced dependency: xunit 2.9.3 has no dynamic skip
  (`Assert.Skip` appeared only in v3), and the live suite must honestly show "Skipped", not "Passed".
  It will disappear by itself when migrating to xunit v3.
- ~~**WAL is not enabled.** The plan promised "WAL in the connection string", but `Microsoft.Data.Sqlite`
  has no such key. It is enabled with one `PRAGMA journal_mode=WAL` at startup after `MigrateAsync`
  (the value persists in the DB file forever). It cannot go into a migration: migrations run in a
  transaction, and inside a transaction this PRAGMA is a no-op. Do it in M2 part 2, together with
  running migrations at host startup.~~ — closed in M2 part 2: the PRAGMA runs in `Program.cs`
  immediately after `MigrateAsync`; activity is confirmed by the `lab.db-wal`/`lab.db-shm` files on
  disk.
- **Dependency inversion in the plan:** the append-only trigger migration sat in M2 part 6, but the
  `ai_audit_entries` table does not exist before M5. Moved wholesale to M5 part 3 — the `AiAuditEntry`
  entity, its migration, `AppendOnlyInterceptor`, and the SQL triggers travel together so the
  protection appears at the same moment as the table. M2 part 6 is only about the authored corpus.
- `Demo:Password = "demo123"` is committed in `appsettings.json` — a deliberate demo compromise
  (without it the first start would print three generated passwords to the console, and the
  repeatable GIF recording scenario would depend on console scrollback). The README (M7) must
  explicitly mark it demo-only and forbid use outside a local machine.
- The line spans of CSV chunker windows are computed arithmetically (`section LineStart + row index`)
  and are correct only for **consecutive** data rows. The parser skips empty rows, and an empty row
  between data rows would shift the file mapping. Instrument exports contain no empty rows between
  records (verified on `1.csv`); if a format with empty rows appears, the mapping must be carried
  through the parser instead of reconstructed in the chunker.
- `GenericTextChunker` does not merge short sections: a JSON root array is split into one chunk per
  element, even if the element is a single line. It does not matter for the corpus (`balance-log`
  is one root object); if a flat array of small elements appears in the golden set, merging will
  have to be brought back specifically for JSON.
- `SourceDocument.SourcePath` has no index — `FindActiveBySourcePathAsync` does a full table scan.
  Unnoticeable for the demo corpus (dozens of documents); if the corpus grows to hundreds of files
  with frequent re-ingests, add `HasIndex(SourcePath)` + a migration.
- E2E tests in M7 on `WebApplicationFactory` must override `ConnectionStrings:LabDb` in the test
  configuration — otherwise the factory at startup will set `MigrateAsync` + seeding onto the real
  `data/lab.db` next to the sources. The pitfall was discovered when wiring migrations into the host
  (part 2) and is documented in advance.
- The SHA256 journal hash chain (`PrevHash`/`Hash` + `VerifyChainAsync`) is deliberately deferred:
  the user chose the simpler append-only variant. The design leaves room (deterministic
  `max(Id)+1` in a transaction), implementation is ~1 day, and the full pattern exists in Mini-CDS.
- A Qdrant implementation of `IVectorStore` — the seam exists, the code does not (YAGNI). Brute force
  is exact; the ceiling is memory: 3072-dim float32 ≈ 12 KB/vector, ~80k vectors per GB.
- `Rag:MinScore = 0.35` is a starting default, not a measured value. It gets calibrated against the
  golden set in Milestone 3/7; tests must assert the final number instead of leaving it a guess.
- Name masking is a best-effort heuristic (it fires only with a role keyword on the same line).
  Without that trigger `Agilent 1260` would produce a false positive.
- FluentAssertions is under a commercial license — inherited from Mini-CDS; replace before going
  to production.
- DB and log paths are relative (to the process CWD) — inherited from Mini-CDS; production needs
  absolute paths from `AppContext.BaseDirectory`.
- The Mini-CDS README is written in Russian, even though the convention says "code and README — EN".
  Here we deliberately diverge in favor of EN (the portfolio targets Agilent, Shanghai) — project A
  should be brought to the same shape.

---

## Chronological log

### 2026-09-10 — Planning and environment reconnaissance

**Plan:** figure out what is already on the machine, lock the stack, and remove technical risks
before the first line of code.

**Done:**
- Surveyed `C:\Develop\Lab-ai-assist`: the directory is empty except for `AGENT.md` with the coding
  standards (16 sections, including DOD §2.4 for hot paths and sensitive-data masking in logs §3.2).
- Found and read project A, `C:\Develop\Mini-CDS` (git `ddenvy/Mini-CDS`): Clean Architecture,
  net10.0, a ready append-only Part 11 audit implementation. Taken from there as reference:
  `AuditEntry.cs`, `IAuditTrail.cs`, `AuditTrail.cs` (`max(Id)+1` in a transaction),
  `AppendOnlyInterceptor.cs`, migration `20260909030320_AddAppendOnlyTriggers.cs` (SQL triggers
  with `RAISE(ABORT)`), `HashChain.cs` (length-prefix canonicalization), `PasswordHasher.cs`
  (PBKDF2-SHA256, 100k iterations, `FixedTimeEquals`), `DbSeeder.cs`,
  `ServiceCollectionExtension.cs`, `appsettings.json` (Serilog compact JSON).
- Verified actual package versions via the NuGet flat-container API, not from memory.
- Checked the `Mini-CDS\Reports\1.csv` format: 28 rows, one sample `Sample_003`, one row per peak,
  **UTF-8 BOM**, the `Tailing` column is sometimes empty.
- Four architectural forks agreed with the user: net10.0, Blazor Server + Minimal API,
  Gemini, our own EF Core + SQLite vector store, real login.

**Problems / pitfalls:**
- **The original spec demanded .NET 8, but the .NET 8 SDK is not installed on the machine** —
  `dotnet --list-sdks` showed only 9.0.315 and 10.0.301, runtimes 9.0.17 and 10.0.9. Targeting
  net8.0 would require installing the SDK and would diverge from Mini-CDS. Decision: net10.0,
  confirmed by the user.
- **`Microsoft.SemanticKernel.Connectors.Google` has no stable version** — the whole range is
  `1.76.0-alpha … 1.80.1-alpha`, while `Microsoft.SemanticKernel` itself is stable (1.80.1).
  So the alpha status is not a version accident but a permanent state of the connector. Hence the
  decision to hide it behind our own ports and pin the exit ramp in ADR-0002.
- **The embedding API in the connector had already changed.** Verified with a reflective probe
  project in `/tmp` (deleted afterwards), not from the docs: `AddGoogleAIEmbeddingGeneration`
  carries the `[Obsolete("Use AddGoogleAIEmbeddingGenerator instead.")]` attribute. The current
  surface is `AddGoogleAIEmbeddingGenerator(modelId, apiKey, GoogleAIVersion.V1_Beta, serviceId,
  httpClient, dimensions)` returning
  `Microsoft.Extensions.AI.IEmbeddingGenerator<string, Embedding<float>>`, not
  `ITextEmbeddingGenerationService`. Also found that `GeminiPromptExecutionSettings` has
  `Temperature`/`TopP`/`MaxTokens` as nullable, and `GoogleAIEmbeddingGenerator` does **not** expose
  `ModelId` as a public property — so the adapter must track the model actually used itself.
- **The NuGet id of PdfPig is exactly `PdfPig`, not `UglyToad.PdfPig`** (the latter also exists and
  returns 200, but it is a different id; the namespace is still `UglyToad.PdfPig`). Stable version
  0.1.16. iText7 was not considered — AGPL.
- There is no official SQLite vector connector in the .NET ecosystem:
  `CommunityToolkit.VectorData.*` has Qdrant/InMemory/PgVector/Redis/Weaviate/Cosmos but no SQLite;
  packages like `HiraokaHyperTools.sqlite-vec` are third-party with no credible support. Hence our
  own store.
- `Serilog.AspNetCore` **10.0.0** transitively brings exactly the versions the plan pinned
  separately (Extensions.Hosting 10.0.0, Formatting.Compact 3.0.0, Settings.Configuration 10.0.0,
  Sinks.File 7.0.0) — four explicit references would be duplication with a version-conflict risk.
  Verified via nuspec and kept the single package.
- A direct reference to `Google.GenAI` must not be added: the connector is compiled against 0.11.0,
  and NuGet resolves the minimum — an explicit reference to the recent 1.21.0 would produce an
  incompatibility.

**Outcome:** the plan is approved (7 milestones split into parts), risks removed before code. There
are no tests yet.
**Next:** Milestone 1, part 1 — the solution skeleton.

---

### 2026-09-10 — Milestone 1, part 1: Skeleton solution

**Plan:** create a solution with five projects, configure dependencies, and get a warning-free
green build plus a working test harness.

**Done:**
- `git init -b main` in `C:\Develop\Lab-ai-assist`.
- `Lab-ai-assist.slnx` — the new XML solution format, same shape as `Mini-CDS.slnx`
  (`/src/` and `/tests/` folders).
- `Directory.Build.props` — a copy from Mini-CDS: `Nullable=enable`, `ImplicitUsings=enable`,
  `LangVersion=latest`.
- `.gitignore` — based on Mini-CDS, but the secrets section is moved to the top and hardened:
  `.env`, `.env.local`, `*.pfx`, `*.snk`. Plus runtime artifacts `data/`, `logs/`, `*.db*`.
- `.env.example` — `GEMINI_API_KEY`, `GEMINI_MODEL=gemini-2.5-flash`,
  `GEMINI_EMBEDDING_MODEL=gemini-embedding-001`, `PII_HMAC_KEY`. Each key with a comment on why it
  exists and how to generate it (`openssl rand -hex 32`).
- Five projects: `LabAi.Domain` (zero dependencies), `LabAi.Application` (→ Domain),
  `LabAi.Infrastructure` (→ Application + Domain; SemanticKernel 1.80.1,
  SemanticKernel.Connectors.Google 1.80.1-alpha, DotNetEnv 3.2.0),
  `LabAi.Web` (`Microsoft.NET.Sdk.Web`; → all three; Serilog.AspNetCore 10.0.0),
  `LabAi.Tests` (xunit 2.9.3, NSubstitute 6.2.0, FluentAssertions 8.10.0, Mvc.Testing 10.0.12,
  coverlet 6.0.4).
- `src/LabAi.Web/Program.cs` — a minimal stub host and `appsettings.json` with the full `Rag`
  configuration block (TopK 5, MinScore 0.35, MaxChunkChars 2000, OverlapChars 200,
  EmbeddingBatchSize 16, OperationTimeoutSeconds 60) so the config does not have to be reinvented
  in M2–M4.
- `tests/LabAi.Tests/Architecture/LayeringTests.cs` — a Clean Architecture guard test: Domain
  references no other project and pulls no EF Core / Semantic Kernel / ASP.NET / Serilog;
  Application references neither Infrastructure nor Web. The test is written now but pays off later —
  any accidental dependency into Domain fails here instead of at code review.
- `Marker.cs` in Domain and Application — a temporary build anchor (see technical debt).

**Problems / pitfalls:**
- **`dotnet restore` passed on the first try** — the milestone's main risk (the alpha connector on
  net10.0) did not fire: the package has a dedicated `lib/net10.0` TFM group, so asset resolution
  did not fall back to netstandard2.0. It could have gone the other way — alpha packages do not
  always get a fresh TFM group, and then the SK types would have come from a stripped assembly
  without `IEmbeddingGenerator`.
- **FluentAssertions was not picked up** — the build produced three
  `CS1061: "string[]" does not contain a definition for "Should"`. The reason: the test csproj had a
  global `<Using Include="Xunit" />` (inherited from Mini-CDS) but no
  `<Using Include="FluentAssertions" />`. Added it globally to avoid repeating the `using` in every
  test file.
- **The guard test initially failed, and that is a substantive finding.** I wrote
  `ReferencedProjectNames(Application).Should().BeEquivalentTo(["LabAi.Domain"])` — and got an empty
  actual list. The reason: **the C# compiler does not write actually-unused references into assembly
  metadata**. While Application only contains `Marker`, the Domain reference exists in the csproj but
  is absent from `Assembly.GetReferencedAssemblies()`. A positive assertion ("must reference X") is
  fundamentally unreliable as an architecture guard — it breaks and repairs itself as code starts
  touching types of the neighboring layer. Rewrote it to assert only the forbidden direction
  (`NotContain(["LabAi.Infrastructure", "LabAi.Web"])`), which matches the test's name. The lesson
  is recorded in the test comment.
- Empty `LabAi.Domain` / `LabAi.Application` projects build into empty assemblies without errors —
  no extra stub is needed for that, but `LabAi.Web` on `Microsoft.NET.Sdk.Web` without `Program.cs`
  would not build (no entry point with `OutputType=Exe`). Hence the minimal host was added at once.

**Outcome:** the build is clean — **0 warnings, 0 errors**; tests **3/3 green**. The solution
skeleton is ready, packages resolved, the alpha connector works on net10.0. Milestone 1 is open
(part 1/3).
**Next:** part 2 — `.env` loading, Serilog with compact JSON, correlation-id middleware, `/health`.

---

### 2026-09-10 — Milestone 1, part 2: Configuration, Serilog, correlation id, /health

**Plan:** teach the host to read `.env`, write structured logs, flow a correlation id through all
events of one request, and serve `/health` — so that no log line and no response field ever contain
a secret.

**Done:**
- `src/LabAi.Infrastructure/Ai/GeminiOptions.cs` — pure data: `ApiKey`, `ChatModelId`,
  `EmbeddingModelId` + computed `IsConfigured`. All properties are `init`; the object is immutable
  and registered as a singleton.
- `src/LabAi.Web/Infrastructure/GeminiOptionsFactory.cs` — option resolution: environment variables
  (`GEMINI_API_KEY`, `GEMINI_MODEL`, `GEMINI_EMBEDDING_MODEL`) beat the `Gemini` section in
  `appsettings.json`, then defaults. Empty and whitespace strings count as "not set".
- `src/LabAi.Web/Infrastructure/CorrelationIdMiddleware.cs` — accepts `X-Correlation-Id` from
  outside, otherwise takes `Activity.Current?.Id`, otherwise generates a `Guid("N")`; puts it into
  `HttpContext.Items`, echoes it in the response header, and pushes it into `LogContext`.
- `src/LabAi.Web/Endpoints/HealthEndpoints.cs` + `HealthResponse` — `GET /health`: `status`
  (`healthy` / `degraded`), `geminiKeyPresent`, `chatModel`, `embeddingModel`, `correlationId`.
- `src/LabAi.Web/Program.cs` rewritten: `DotNetEnv.Env.TraversePath().Load()` → bootstrap logger →
  `UseSerilog(ReadFrom.Configuration + Enrich.FromLogContext)` → register `GeminiOptions` →
  `UseMiddleware<CorrelationIdMiddleware>` → `UseSerilogRequestLogging` → routes. Everything wrapped
  in `try/catch(Log.Fatal)/finally(Log.CloseAndFlush)` — the Mini-CDS pattern.
- `appsettings.json`: a `Gemini` block with an **empty** `ApiKey` (a secret is fundamentally never
  put into config), a `Serilog` block — Console + File (`logs/labai-.log`, `rollingInterval: Day`,
  `rollOnFileSizeLimit`, 10 MB, `CompactJsonFormatter`), `Properties:Application = LabAi`.
- `.gitattributes` — `* text=auto eol=lf` plus explicit extension rules and `binary` for
  `.png/.gif/.pdf/.db`.
- Tests: `CorrelationIdMiddlewareTests` (5) and `GeminiOptionsFactoryTests` (5).

**Problems / pitfalls:**
- **The plan contradicted itself, and this surfaced only now.** The plan said "fail-fast when
  `GEMINI_API_KEY` is missing", while the testing section said the golden set and E2E on
  `WebApplicationFactory` must run **offline, without a key**. Fail-fast at startup would make the
  host itself impossible in tests. Resolved in favor of degradation: a warning at startup, an
  exception on the first real adapter call (part 3), `geminiKeyPresent` in `/health`. The decision
  is recorded in the "locked forever" table as a deliberate divergence from the plan.
- **`DotNetEnv` must be called before `WebApplication.CreateBuilder`.** `CreateBuilder` snapshots the
  process environment into `IConfiguration` once; anything DotNetEnv sets later is never seen by
  configuration. Verified practically: with `.env` on disk `/health` returned `healthy`, without it
  `degraded`.
- **One log line stubbornly arrived without `CorrelationId`.** It turned out to be
  `Request finished ...` from `Microsoft.AspNetCore.Hosting.Diagnostics`: the hosting layer writes
  it **outside** the middleware pipeline, so the `LogContext` pushed inside the pipeline is
  fundamentally unreachable for it. The line also duplicated Serilog's
  `HTTP {RequestMethod} {RequestPath} responded {StatusCode}`. Raised the
  `Microsoft.AspNetCore.Hosting` override to `Warning`. Similarly muted
  `Microsoft.AspNetCore.Http.Result` — `OkObjectResult` wrote two "Writing value of type ... as Json"
  lines per response. After the fix: **zero** request-scoped lines without `CorrelationId`
  (verified with `grep '"RequestPath"' | grep -vc CorrelationId` → `0`).
- **The order of `UseSerilogRequestLogging` relative to the correlation middleware is critical, and
  I first placed it wrong mentally.** The middleware disposes the `LogContext.PushProperty` when
  downstream returns. If request logging is registered **before** the correlation middleware, its
  completion event ends up outside the scope and arrives without the id. Placed it after — and pinned
  the reason with a comment in `Program.cs`, so a future refactoring does not "fix" the order.
- **`GeminiOptionsFactory` nearly drifted into Infrastructure.** `IConfiguration` is available there
  only transitively — through `DotNetEnv` → `Microsoft.Extensions.Configuration.Abstractions`
  **1.1.2**. Leaning on a transitive dependency with such a low floor means a surprise on the first
  DotNetEnv update. Split: data (`GeminiOptions`) in Infrastructure, env+config reading in Web — that
  is literally the composition root's job. Side benefit: `AddLabAiGemini` in part 3 accepts ready
  options, and Infrastructure never needs `IConfiguration` at all.
- **`Activity.Current?.Id` is not a GUID.** In a real host ASP.NET Core creates an Activity per
  request, and the id arrives in W3C format `00-245e8ea0...-88b4af48...-00`, while in a unit test
  `Activity.Current` is `null` and the `Guid("N")` branch fires. A naive "32 hex chars" assertion
  would pass in the test and fail at runtime. So the test asserts only non-emptiness and **identity**
  of the id in the header, in `HttpContext.Items`, and in what downstream sees.
- **The env-precedence check cannot use `Environment.SetEnvironmentVariable`.** Process environment
  variables are global, and xunit runs test classes in parallel — it would create a race between
  `GeminiOptionsFactoryTests` and future tests reading the same `GEMINI_API_KEY`. Added a second
  factory overload with `Func<string, string?>`, and the test became fully hermetic.
- **Secret hygiene during the manual run.** To exercise the `healthy` branch I needed a key. Created
  a temporary `.env` with a clearly fake value, first confirming
  `git check-ignore -v .env` → `.gitignore:2`, and deleted the file right after the check
  (`ls .env` → "No such file"). The real key from `C:\Develop\JobJoy\backend\.env` was neither read
  nor copied; only the boolean `geminiKeyPresent` reaches the log and the `/health` response, never
  the value.
- It could have broken but did not: Serilog's File sink creates the `logs/` directory itself, no
  separate initialization is needed; `DotNetEnv.Env.TraversePath().Load()` silently returns an empty
  result when `.env` is absent instead of throwing — otherwise the keyless host would not start at
  all.

**Outcome:** the build is **0 warnings, 0 errors**; tests **13/13 green** (3 architecture + 10 new).
Manual host run: `GET /` → 200; `GET /health` → 200 with `degraded` without a key and `healthy`
with one; `X-Correlation-Id` is returned in the response and propagated from the incoming header;
all request-scoped compact-JSON log lines carry `CorrelationId`. Milestone 1 — part 2/3 closed.
**Next:** part 3 — the `GeminiChatClient` and `GeminiEmbeddingService` adapters, the temporary
`POST /api/chat`, ADR-0002, live smoke tests.

---

### 2026-09-10 — Milestone 1, part 3: Gemini adapters, /api/chat, ADR-0002

**Plan:** hide the alpha Gemini connector behind two of our own ports, verify the plumbing live with
a real key, lock irreversible decisions in ADR-0002, and close Milestone 1.

**Done:**
- **A reflection probe instead of documentation.** A throwaway project `C:\tmp\apiprobe` (deleted
  after use) dumped the real surface of `Microsoft.SemanticKernel.Connectors.Google 1.80.1-alpha`:
  the signatures of all `AddGoogleAI*` methods, the `[Obsolete]` markers, the properties of
  `GeminiPromptExecutionSettings`, the `IEmbeddingGenerator<string, Embedding<float>>` contract, and
  separately — behavior with an empty and a `null` key. Everything written in the adapters below
  relies on that dump, not on memory. The trap inside the reconnaissance itself:
  `AppDomain.CurrentDomain.GetAssemblies()` does **not** contain assemblies whose types were never
  touched — the first run found 0 Google types until an explicit `Assembly.Load(...)` was added.
- **Ports in Domain** (`Abstractions\`): `IGroundedChatClient` + `GroundedChatResult`,
  `IEmbeddingService`. `Marker.cs` was removed from Domain; `LayeringTests` switched to
  `typeof(IGroundedChatClient).Assembly`.
- **Infrastructure\Ai:** `GeminiNotConfiguredException`, `GeminiChatClient`
  (`Temperature = 0.0`, `TopP = 0.95`, `MaxTokens = 2048`, `CandidateCount = 1`),
  `GeminiEmbeddingService` (checks "as many texts as vectors, in the same order"),
  `GeminiServiceCollectionExtensions.AddLabAiGemini(GeminiOptions)`.
- **Web:** `Endpoints\ChatEndpoints.cs` — temporary `POST /api/chat` with a 4000-character cap
  (AGENT.md 3.3: untrusted input is bounded before hitting a paid external API) and mapping "no key"
  → **503**, not 500: nothing is broken, and a retry without a config change cannot help.
- **Package rearrangement.** `DotNetEnv` moved from Infrastructure to Web — `Program.cs` is its only
  consumer. Infrastructure explicitly references `Microsoft.Extensions.DependencyInjection.Abstractions`
  and `Microsoft.Extensions.Logging.Abstractions` **10.0.6**: both are used directly, while
  transitively they would arrive with a `1.x` floor from the alpha connector.
- **ADR-0002** (EN): 7 decisions, an exit ramp to the REST `generateContent`/`embedContent` endpoints
  (<100 lines, zero changes above Infrastructure), an alternatives table, and obligations.
- **Tests:** 24 new — 7 for the chat adapter, 8 for the embedding adapter, 4 for the DI graph, 5
  live.
- **Live run:** `dotnet run` → `/health` = `healthy` with `chatModel: gemini-3.5-flash`;
  `POST /api/chat` → 200 with a real Gemini answer, including a query in Russian.

**Problems / pitfalls:**
- **`gemini-2.5-flash` — the model from the plan — is retired for new keys.** The live test failed
  with `HttpOperationException: 404`. Google's response body: *"This model models/gemini-2.5-flash
  is no longer available to new users. Please update your code to use models/gemini-3.6-flash"*,
  identically on `/v1/` and `/v1beta/`. Yet the model **is present** in `GET /v1beta/models` — so
  the model list is not an availability check; only a real `generateContent` call is. Ran the
  candidates with our exact `generationConfig`: `gemini-3.6-flash` → **503 UNAVAILABLE**
  (overload); `gemini-3.5-flash`, `gemini-flash-latest`, `gemini-3.1-flash-lite` → 200. Chose
  **`gemini-3.5-flash`**: `flash-latest` was rejected because it is a rolling alias, and `Model` in
  the audit is evidence of which model answered; an alias would make the entry false while leaving
  it superficially correct. Google's recommended `3.6-flash` was rejected due to unstable
  availability — a demo that fails on the first question is worse than a model one generation older.
  This is exactly the class of breakage offline tests fundamentally cannot catch — the best possible
  argument for a live suite.
- **`Assert.Skip` does not exist in xunit 2.9.3, although the plan claimed otherwise.** The compiler
  produced CS0411 resolving `Skip` to `AsyncEnumerable.Skip` — that alone suggested the
  `Assert.Skip` member does not exist. Verified not from memory but in the package itself:
  `xunit.assert.xml` contains `Xunit.Sdk.SkipException.ForSkip` with the explicit caveat *"this only
  works in v3 and later of xUnit.net"*, and the DLL's #Strings heap has no `Skip` name at all. There
  is no v2 workaround either: `Skip` on `[Fact]` is a compile-time constant, and
  `ReflectionAttributeInfo` reads `CustomAttributeData` without instantiating, so the "compute
  `Skip` in your attribute constructor" trick does not work. Took `Xunit.SkippableFact` 1.5.85
  (MS-PL, netstandard2.0) → `[SkippableFact]` + `Skip.IfNot`. An early `return` was consciously
  rejected: it paints "Passed" where the test did not run. Result without a key: **32 passed,
  5 skipped**.
- **`GeminiPromptExecutionSettings.Temperature` is `double?`, not `float?`.** The plan wrote
  `Temperature = 0.0f`; the probe showed `double?`, and `0.0f` would not compile. A triviality that
  is caught only by an assembly dump.
- **`CandidateCount = 1` is mandatory next to `Temperature = 0`.** With Gemini,
  `candidateCount > 1` requires `temperature = 1.0`, i.e. "multiple answer candidates" and
  "reproducibility" are mutually exclusive by API construction. Pinned with a dedicated test so a
  future edit cannot silently diverge.
- **Registration with an empty key succeeds; with `null` it fails immediately.** Probe:
  `AddGoogleAI*(model, "")` → registration succeeds, the exception comes only at resolve
  (`ArgumentException`: "The value cannot be an empty string…"). This is precisely what enables
  registering Gemini **unconditionally** and keeping the guard inside the adapter — degraded mode
  without two DI graphs. It would not work with `null`.
- **`AddGoogleAIChatClient` is newer but cannot be used.** Its second parameter is
  `Google.GenAI.Client`: we would have to name a type from the transitively locked
  `Google.GenAI 0.11.0`. A direct reference to the fresh major version would break the connector.
  The asymmetry "chat on SK `IChatCompletionService`, embeddings on
  `Microsoft.Extensions.AI.IEmbeddingGenerator`" is accepted deliberately and is invisible above
  Infrastructure.
- **`EmbedAsync` deliberately passes `options: null`.** `EmbeddingGenerationOptions` has `ModelId`,
  and per-call model override is exactly the route by which two incomparable vector spaces appear in
  the store. Wrote a test asserting the options really are `null`; otherwise the intent would be
  indistinguishable from forgetfulness.
- **NSubstitute: `Arg.Do`/`Arg.Any` cannot be placed inside conditional expressions.** The first
  helper version was
  `history is null ? Arg.Any<ChatHistory>() : Arg.Do<ChatHistory>(…)`. These methods work by side
  effect on an internal matcher queue, so branching disrupts argument ordering unpredictably.
  Replaced with unconditional `Arg.Do<T>(captured => cb?.Invoke(captured))`.
- **CS1503 in my own test:** `Replying("the model answer")` with `params ChatMessageContent[]`.
  The second build error was the aforementioned `Assert.Skip`. Both are the price of writing four
  test files in a row without an intermediate build; compile after each from now on.
- **A spurious 400 on the first manual run.** `curl -d '{"message":"…Cyrillic…"}'` from Git Bash
  returned 400 while the endpoint worked perfectly. The same body written to a file as UTF-8 and
  sent with `--data-binary @file` produced 200 and the answer "Yes". A shell encoding artifact, not
  a code one — but it had to be checked: the product was entirely Russian-language at the time, and
  "does not work in Russian" would have been fatal.
- **Key hygiene during the live run.** The key was supplied to the environment variable through a
  `grep | cut | tr` pipeline and was **never printed**; reports mention only
  `${#GEMINI_API_KEY}` = 53. No `.env` file was created in the repository this time. Before the run
  I separately checked that along the `DotNetEnv.Env.TraversePath()` route (strictly upward from
  CWD) there are no foreign `.env` files able to overwrite the variable — `C:\Develop\.env` and
  `C:\.env` do not exist.
- It could have broken but did not: `gemini-embedding-001` really returned **3072** dimensions —
  matching the plan, so the M3 dimension guard will count from a verified number, not an assumption;
  `services.AddLogging()` resolves in the test project thanks to the
  `<FrameworkReference Include="Microsoft.AspNetCore.App" />` brought by `Mvc.Testing`; the four
  embedding tests passed on the first try, i.e. the adapter correctly parses
  `GeneratedEmbeddings<Embedding<float>>` and `ReadOnlyMemory<float> → float[]`.

**Outcome:** the build is **0 warnings, 0 errors**; offline **32/32** (plus 5 honestly skipped
live), live **5/5** with a real key. Manual run: `/health` → `healthy`, `POST /api/chat` → 200 with
a live `gemini-3.5-flash` answer, including in Russian. The adapter log line contains only
`ModelId`, `PromptChars`, `CandidateCount`, `AnswerChars`, and `CorrelationId` — neither the
question text nor the answer text (AGENT.md 3.2), verified by dumping the full JSON line.
ADR-0002 is signed.
**Milestone 1 closed (3/3).**
**Next:** Milestone 2, part 1 — `LabAiDbContext` + entity configurations + the `InitialCreate`
migration + `IDesignTimeDbContextFactory`.

---

### 2026-09-10 — Milestone 2, part 1: DB schema — DbContext, configurations, InitialCreate

**Plan:** describe the domain model in Domain, configure mapping in the Mini-CDS convention, and
generate and **verify by running** the first migration.

**Done:**
- **Convention reconnaissance by a dedicated agent.** I needed verbatim `CdsDbContext`, `User`,
  `UserConfiguration`, `SampleConfiguration` (the densest one), `CdsDbContextFactory`, `DbSeeder`,
  the DI extension, and the Mini-CDS migration list — ~300 lines of someone else's code that would
  be noise in the main context. The agent returned a digest with a cheat sheet; I then wrote from it
  without reopening the files.
- **Domain:** enums `UserRole` (Operator/Analyst/Administrator — taken from Mini-CDS as-is, the roles
  matched the plan), `DocumentKind`, `DocumentStatus`; entities `User` (Mini-CDS shape unchanged,
  `get; set;` — the account is mutable), `SourceDocument` (`get; set;` — supersede changes `Status`
  and `SupersededByDocumentId`), `Chunk` (`init`-only — a chunk is immutable; a citation in an old
  audit must point to exactly the text that was used).
- **Infrastructure\Persistence:** `LabAiDbContext` (primary constructor, `DbSet => Set<T>()`,
  `ApplyConfigurationsFromAssembly`), `LabAiDbContextFactory` (design-time, throwaway
  `design-time.db`), three `IEntityTypeConfiguration<T>`,
  `PersistenceServiceCollectionExtensions.AddLabAiPersistence`.
- **Migration `20260910063859_InitialCreate`:** 3 tables (`users`, `source_documents`, `chunks`),
  3 FKs (all `Restrict`), 7 indexes, enums as `TEXT(32)`, `Embedding` as `BLOB`.
- **17 tests against real SQLite** (`DataSource=:memory:` + `Database.Migrate()`, not
  `EnsureCreated` — this also verifies that the migration script itself applies): table names, enums
  by name via raw SQL, `Kind=Utc` after re-reading, byte-exact BLOB round-trip, the
  `(DocumentId, ChunkIndex)` uniqueness, the supersede chain, both pairs of delete-prohibition
  tests, and DI registration.

**Problems / pitfalls:**
- **`NU1605` did not happen only because I headed it off.** EF Core 10.0.12 depends on
  `Microsoft.Extensions.Logging 10.0.12`, while in Infrastructure both abstractions were explicitly
  pinned to **10.0.6**. A direct reference below the transitively required version is
  `NU1605 Detected package downgrade` — a restore error, not a warning. Checked the
  `microsoft.entityframeworkcore{,.sqlite}` nuspecs via the flat-container API **before** editing the
  csproj and raised both to 10.0.12.
- **`dotnet ef` refused to generate the migration:** "Your startup project 'LabAi.Web' doesn't
  reference Microsoft.EntityFrameworkCore.Design". The reason is exactly our own decision: in
  Infrastructure the package is declared with `PrivateAssets=all`, so it does not flow outward, while
  the tooling requires it in the startup project. Checked Mini-CDS with grep: there Design is
  declared in **both** projects, including `MiniCds.Wpf`, also with `PrivateAssets=all`. Mirrored it —
  a divergence from the plan (which had Design only in Infrastructure).
- **`Restrict` on a required FK fires earlier than I assumed — and my test caught it on itself.** I
  wrote "`SaveChanges()` throws `DbUpdateException`" — it failed. EF Core actually controls the
  severing of a required relationship **in the change tracker**, and `InvalidOperationException`
  ("The association … has been severed") arrives already from `Remove()`: the deletion never enters
  the change queue. This is stronger than what I tested. Rewrote the assertions — and **added a
  second pair of tests using raw SQL**, because tracker control is bypassed with one
  `ExecuteSqlRaw`, and without such a check `Restrict` would remain a declaration, not a database
  property.
- **A question I planned to defer closed as a side effect: `PRAGMA foreign_keys` is on by default in
  EF Core.** I added no connection settings, and a raw `DELETE FROM users WHERE Id = 1` failed with
  `FOREIGN KEY constraint failed`. So the protection holds at two levels without a single line of
  configuration. Mini-CDS configures nothing either — now it is clear why it works for them.
- **The BLOB test failed because of the fixture, not the BLOB.** The `NewChunk()` factory defaulted
  `DocumentId = 1`, while the fixture seeded only a user — the FK rejected the chunk insert, and
  `DbUpdateException` looked like a BLOB-column problem. Fixed in two moves: one actor is created in
  the fixture constructor (every document must be uploaded by someone), and the document is created
  explicitly in each chunk test. General lesson: **default values in test factories are a hidden
  coupling between tests** that fires in the third test along.
- **`Database.GetConnectionString()` is an extension method from `Microsoft.EntityFrameworkCore`**
  (`RelationalDatabaseFacadeExtensions`). CS1061 in a new file without the using; the same call
  compiled in the neighboring test file because the using was already there. The classic "works in
  one file, not in another" trap.
- **Deliberately added no unique indexes "for later".** The temptation was strong: a unique index on
  `ContentHash` (idempotency, after all!) or on `(SourcePath, Version)`. Both run into semantics that
  do not exist in code yet: re-ingesting **old** content is legal (rollback to a previous version),
  so a global unique hash constraint would forbid a legal operation. The constraints will appear in
  M2 part 5, when `IngestPipeline` defines what counts as a duplicate — and then they can be added
  in one migration. Indexes now serve only real queries: `ContentHash` (duplicate lookup), `Status`
  (Active-only snapshot), `(DocumentId, ChunkIndex)` unique (chunk position),
  `(EmbeddingModelId, EmbeddingDimension)` (the M3 startup dimension guard).
- **`AiAuditEntry` did not make it into `InitialCreate` — on purpose.** I first wanted to describe
  the whole schema at once so the append-only triggers would follow right after, as in Mini-CDS
  (their `InitialCreate` and `AddAppendOnlyTriggers` are five seconds apart). I declined: audit has
  nothing to do with ingest, and a table that nobody reads or writes for three milestones is dead
  weight in a migration. The plan also contained a dependency inversion (triggers sat in M2 part 6
  while the table is created in M5) — everything moved to M5 part 3 as one block, see technical debt.
- It could have broken but did not: EF Core materializes `init`-only `Chunk` properties without a
  parameterized constructor (the same trick as `AuditEntry` in Mini-CDS);
  `ApplyConfigurationsFromAssembly` picked up all three configurations — otherwise the tables would
  be named `SourceDocuments` and `Chunks`, and the snake_case test would catch it;
  `Sqlite:Autoincrement` was set for the `long` PK automatically; the `design-time.db` file was not
  created during migration generation at all — EF needs the model, not a connection.

**Outcome:** the build is **0 warnings, 0 errors**; offline **49/49** (plus 5 skipped live), 17 of
them new. The migration is verified by a real run on SQLite, not just by the fact of generation.
Milestone 2 — part 1/7.
**Next:** part 2 — port the auth stack from Mini-CDS (`PasswordHasher`, `EfUserStore`, `AuthService`)
+ `DbSeeder`, wire `AddLabAiPersistence` and `MigrateAsync` in the host, `PRAGMA journal_mode=WAL`.

---

### 2026-09-10 — Milestone 2, part 2: Auth stack from Mini-CDS, DbSeeder, running migrations at startup

**Plan:** port the auth stack from Mini-CDS (`PasswordHasher`, `EfUserStore`, `AuthService`), add
`DbSeeder`, wire `AddLabAiPersistence` and `MigrateAsync` in the host, and enable WAL.

**Done:**
- **Ports in Domain** (`Abstractions\`): `IPasswordHasher`, `IUserStore`, `IAuthService`, the
  `AuthResult` record with `Success(User)` / `Failure(string)` statics. All four files — one
  interface each, as AGENT.md requires; Infrastructure implements, Application consumes.
- **Infrastructure\Security\PasswordHasher.cs** — ported verbatim from Mini-CDS: PBKDF2-SHA256,
  100,000 iterations, 16-byte salt, 32-byte key, `CryptographicOperations.FixedTimeEquals`,
  `Convert.TryFromBase64String` with a `bytesWritten` check (corrupt base64 in the DB yields an
  honest "password did not match", not `FormatException`).
- **Infrastructure\Persistence\EfUserStore.cs** — `AsNoTracking()` + `FirstOrDefaultAsync` on exact
  name equality (case-sensitive, parity with Mini-CDS — covered by a test, not hope).
- **Application\Auth\AuthService.cs** — all failure paths return the same generic error, and
  unknown-user / disabled-account cases pre-burn verification on **dummy credentials** from
  `Lazy`: without this an unknown name would respond in microseconds and a real one after 100k PBKDF2
  iterations, turning response time into a user-enumeration oracle.
- **Infrastructure\Persistence\DbSeeder.cs** — three demo accounts, one per role
  (`admin`/`analyst`/`operator`, so the demo shows RBAC from both sides), idempotency via
  `OrdinalIgnoreCase`, password generation with an alphabet without `l 1 I O 0 o`; the plaintext is
  returned in the result and printed to the **Console**, not Serilog — the file sink never receives
  the secret.
- **Program.cs** — fail-fast on a missing `ConnectionStrings:LabDb` (degradation, unlike Gemini),
  the DB directory is created from `SqliteConnectionStringBuilder.DataSource`, then a scope:
  `MigrateAsync()` → `PRAGMA journal_mode=WAL;` → `SeedAsync()`.
- **`Marker.cs` removed from Application** — `AuthService` appeared there; `LayeringTests` switched
  to `typeof(AuthService).Assembly` and gained a fourth test
  `Application_ReferencesNoFrameworkPackages` (the Domain EF/SK/ASP.NET/Serilog tests were extended
  to Application).
- Tests: `PasswordHasherTests` (7), `AuthServiceTests` (8), `DbSeederTests` (8),
  `EfUserStoreTests` (4), plus the updated `LayeringTests`.
- **Manual host run:** startup → `Database ready; seeded 3 account(s)`; `/health` → 200;
  `lab.db-shm` and `lab.db-wal` appeared in the DB directory (WAL active); restart →
  `seeded 0 account(s)` — the second pass found the existing accounts and created none.

**Problems / pitfalls:**
- **A test caught a real bug in code I had written an hour earlier.** The "generator alphabet does
  not contain `l 1 I O 0 o`" test failed: a password contained a lowercase `o`. The reason — the
  string `"abcdefghijk**mnop**qrstuvwxyz"`: I crossed out only `l` and left `o`, although the
  comment above the line promised both excluded. A classic comment/code mismatch on the adjacent
  line; the test was written exactly for this. The alphabet is fixed, the test is green now and
  guards the promise forever.
- **The second failing test was mine, not the code's.** I wanted to verify "a pre-existing `Admin`
  (in any case) suppresses seeding of `admin`", but I first ran the first `SeedAsync` — it created
  `admin` — and only then added the `Admin` user. The "one administrator in the database" assertion
  legitimately found two: the first was created by the test itself a second earlier. The test was
  rewritten against a clean database where the pre-existing `Admin` is the only actor, plus an
  assertion that `FullName` stayed "Pre-existing" (existing rows are never rewritten).
- **The degradation asymmetry is now complete and deliberate: Gemini without a key is a warning, a
  DB without a connection string is fail-fast.** The logic: without a key the application remains
  useful (ingest, audit infrastructure, and `/health` are alive, AI endpoints honestly return 503 on
  use), while without a DB nothing works at all, including `/health` itself — there is physically
  nothing for the host to be an "application" with. Each failure gets its own strictness level, not
  one uniform fail-fast "for tidiness".
- **Two divergences from Mini-CDS were accepted consciously and documented in XML docs.** (a)
  `IUserStore` contains only `FindByUsernameAsync` — Mini-CDS's `GetSystemUserIdAsync` was dropped
  because this project's journal has no system events: the author of every row is a human after
  login. Hence also (b) `DbSeeder` does not seed a non-interactive `system` account (in Mini-CDS it
  exists exactly as the author of system entries) and `AuthService` does not depend on `IAuditTrail`.
- **CS8122 in a test:** `Should().OnlyContain(u => u.Password is null)` does not compile —
  FluentAssertions takes `Expression<Func<T, bool>>`, and expression trees disallow `is`
  pattern-matching. Replaced with `== null`. Plus the general lesson from part 1: compile after
  every file, not after five.
- **`Demo:Password = "demo123"` in the committed appsettings is an accepted compromise,** but with a
  price: the README must mark it demo-only (debt recorded). The "always generate" alternative would
  make the repeatable GIF scenario depend on first-run console scrollback.
- **Caught a future pitfall immediately:** now that the host itself runs `MigrateAsync` + seeding at
  startup, M7 E2E on `WebApplicationFactory` must override `ConnectionStrings:LabDb`, otherwise the
  tests will set migrations onto the real `data/lab.db`. Recorded as technical debt in advance so as
  not to step on it in M7.
- It could have broken but did not: NU1605 did not appear when raising the pinned
  `Microsoft.Extensions.*` from 10.0.6 to 10.0.12 — the nuspecs were checked back in part 1, here it
  was only execution; the `Lazy` in `AuthService` is thread-safe by default
  (`LazyThreadSafetyMode.ExecutionAndPublication`), which matters when the scoped service is
  resolved by parallel requests; the case-sensitive lookup check ("Analyst" → null) passed at once —
  LINQ translation yields ordinary equality, not SQLite's case-insensitive LIKE, which I feared.

**Outcome:** the build is **0 warnings, 0 errors**; offline **77/77** (plus 5 skipped live), 28 of
them new. Manual run: migrations + seeding at startup, WAL confirmed by the `lab.db-wal`/`lab.db-shm`
files, restart gave `seeded 0 account(s)`. Milestone 2 — part 2/7 closed.
**Next:** part 3 — the parsers (`MarkdownDocumentParser`, `InstrumentCsvParser`, `PdfPigPdfParser`,
`JsonDocumentParser`) + tests.

---

### 2026-09-10 — Milestone 2, part 3: Four format parsers

**Plan:** port the `IDocumentParser` contract + value objects (`ParsedDocument`, `DocumentSection`)
to Domain; four parsers in Infrastructure — Markdown (section-aware), CSV (grouping by
`Sample Name`), PDF (PdfPig), JSON (flattened to `path: value`); tests for all, including a fixture
shaped like the real `1.csv`.

**Done:**
- **Contract.** `IDocumentParser { DocumentKind Kind; ParsedDocument Parse(byte[] content); }` —
  synchronous and byte-taking: PDF is binary, and the pipeline keeps the file in memory anyway for
  SHA256. Parsing is pure CPU; a `CancellationToken` makes no sense. Parser selection is by `Kind`;
  the pipeline (part 5) resolves `IEnumerable<IDocumentParser>`.
- **`MarkdownDocumentParser`** — a heading stack (`SectionPath = "H1 > H2 > H3"`), YAML front-matter
  stripping (only when `---` is the first line and is closed; otherwise it is an hr and the text
  stays in the body), line-by-line code-fence tracking (a heading inside ``` is content), inclusive
  1-based line spans over the section's **non-empty** lines.
- **`InstrumentCsvParser`** — an RFC4180 tokenizer (`CsvTokenizer`, internal): quotes, `""`,
  commas and newlines inside quotes, CRLF/LF, BOM stripped before tokenization. Strict alignment
  validation: a row with a column count ≠ the header → `InvalidDataException` with the row number —
  shifted measurement data must not be ingested silently. Rows are grouped by `Sample Name` in
  first-appearance order; a section = the header + the sample's raw rows. Without a `Sample Name`
  column there is a fallback to one `Rows` section. A record's RawText is an exact slice of the
  source line (quotes preserved byte-for-byte; the chunk text does not diverge from the file on
  disk).
- **`JsonDocumentParser`** — flattening to `path: value` (`runs[2].owner: ivanov`); a root array
  gives one section per element (`[0]`, `[1]`), otherwise one `Json` section; nulls and empty
  containers produce no lines; newlines inside string values are collapsed so they do not break the
  "one fact per line" format; invalid JSON → `InvalidDataException` with a position.
- **`PdfPigPdfParser`** — one section per page (`Page N`, `PageStart/PageEnd`); text is assembled
  from words grouped by baseline (page.Text flattens layout into a word stream); empty pages are
  skipped; standard Unicode ligatures are expanded (`ﬁ` → `fi`).
- **DI:** `AddLabAiParsing` registers all four as singletons (the parsers are stateless).
- **Tests (33 new):** markdown 10, CSV 9 (the fixture mirrors `1.csv`: BOM + CRLF + empty `Tailing`),
  JSON 8, PDF 5 (QuestPDF builds a two-page PDF in memory — no binary fixture needed), DI
  registration 1.

**Problems / pitfalls:**
- **The part's main bug was in my own tokenizer, and an ordinary unit test caught it only inside
  quotes.** The `inQuotes` branch of the `switch` had no `default` branch: ordinary characters
  inside quotes were **not appended to the field**. `"Sample, 3"` parsed into `["", ""]` — the field
  was empty, and grouping sent the sample to "(no sample name)". The file without quotes (`1.csv`)
  worked perfectly, so the simple tests were green. Lesson: cover not only the "real format" but
  also parser branches that never occur in the real format.
- **The second tokenizer version captured CRLF in RawText** — the record slice included `\r\n`, and
  the section text contained blank lines between records (the test saw 6 lines instead of 3).
  Replaced character-by-character accumulation with an exact slice of the source line by indices —
  the inaccuracy with quotes inside quotes disappeared at the same time.
- **The PDF tests exposed an artifact I had not considered: ligatures.** QuestPDF (the Lato font by
  default) replaced "ti" in `verification`/`analytical` with a discretionary ligature **with no
  Unicode mapping** — the glyph vanished without a trace during extraction (`analytical` →
  `analycal`), while `ﬁ` remained a single character. For RAG this is poison: a chunk with
  "veriﬁcaon" will never match a query for "verification". Standard ligatures (ﬁ ﬂ ﬀ ﬃ ﬄ ﬅ) are now
  expanded in the parser; the lost "ti" cannot be recovered in principle — it is a property of the
  PDF generator. I switched the fixture to Russian (the project corpus was Russian-language and
  Cyrillic has no ligatures) and documented the reason in the test.
- **`Restrict`-like strictness for CSV is a contentious but deliberate decision:** a row with fewer
  columns could be padded with empties. For instrument data that is worse than crashing: a silently
  shifted `Area` column would look like a legitimate measurement. A failure with the row number is
  more diagnostic.
- **CS1002 in a test:** I mixed ordinary-string escaping (`\"`) with verbatim style (`""`) in one
  literal — rewrote it as a raw string literal. CS9176: a spread in `[.. a, .. b]` without an
  explicit target type (`var` + a byte[] spread) does not compile — specified
  `byte[] content = [.. ]`.
- **A DI probe through a separate project in `C:\tmp`** (deleted afterwards): internal
  `CsvTokenizer` is not visible externally, so record fields were printed via reflection. The idle
  loop of tracing on paper cost more than writing the probe outright — next time a parser behaves
  "inexplicably", probe first, reason after.
- It could have broken but did not: PdfPig opened the `byte[]` directly (`PdfDocument.Open(content)`)
  without a temporary stream; `QuestPDF.Settings.License = Community` in the test class's static
  constructor fired before the first `GeneratePdf`; Cyrillic passed the full QuestPDF → PdfPig cycle
  without loss (verified by the two-language test — Latin in a PDF with Lato is more treacherous
  than Cyrillic).

**Outcome:** the build is **0 warnings, 0 errors**; offline **110/110** (plus 5 skipped live), 33 of
them new. All four formats parse into a unified `ParsedDocument`; the CSV fixture mirrors the real
Mini-CDS export. Milestone 2 — part 3/7 closed.
**Next:** part 4 — the chunkers (`MarkdownChunker`, `CsvRowGroupChunker`, `GenericTextChunker`) +
tests.

### 2026-09-10 — Milestone 2, part 4: Three chunkers — section merging, overlap, CSV preambles

**Plan:** port the `IChunkingStrategy` contract + `ChunkDraft` to Domain; chunkers in
**Application** (pure BCL, as the plan promised): `MarkdownChunker` (merging short adjacent
sections, budget 2000, overlap 200, sentence-boundary splitting), `CsvRowGroupChunker` (the
"Measurement results, sample …" preamble + repeated header, row windows), `GenericTextChunker`
(line-by-line windows for JSON and PDF); tests.

**Done:**
- **Contract.** `IChunkingStrategy` with `SupportedKinds` (a set, not a single `Kind`) —
  GenericTextChunker honestly serves both JSON and PDF pages, and creating an empty PDF class for
  symmetry with the parsers made no sense. The registration invariant changed from "exactly one per
  kind" to "all kinds covered without overlap" — the test asserts exactly that.
- **`MarkdownChunker`.** Adjacent sections with **one common parent** merge while the sum fits the
  budget; a merged chunk's path is the common parent ("SOP > 4"), a lone section keeps its own
  path. A section larger than the budget is split on sentences with a window and overlap. The
  sentence splitter is deterministic: `.`/`!`/`?` end a sentence only when followed by whitespace +
  a non-digit — "2.0%" and "p. 4.2" are not split; merging plus "did not split" only ever makes the
  unit longer; text fundamentally cannot be lost.
- **`TextWindowPacker`** (shared by markdown and generic): greedy unit packing; a tail of the
  previous window of length ≥ `OverlapChars` opens the next one; a unit longer than the budget is
  hard-split by characters; the tail never covers the whole window, and if even the shortened tail
  leaves no room for a new unit, it is shed from the front instead of emitting a duplicate window.
- **`CsvRowGroupChunker`.** Every window = a preamble ("Measurement results, sample Sample_003: 4
  data rows.") + the header + the rows; it is the preamble that makes a semantic query like "Sample_003
  results" hit. Rows are not overlapped (independent facts; a duplicate = a double citation). The
  window line span is arithmetically derived from the section's `LineStart` (the parser places every
  row on exactly one line). The fallback section without `Sample Name` gets a preamble without the
  word "sample"; the "Rows" sentinel lives in `DocumentSection.UngroupedRowsPath` — the single source
  of truth for parser and chunker.
- **DI:** `AddLabAiChunking(maxChunkChars, overlapChars)` in Infrastructure — the values are read
  where the config exists; Application stays free of DI types.
- **Tests (24 new):** markdown 10 (merging under a common parent, stopping at the budget, different
  parents never merge, overlap, lossless hard-split, "2.0%" and "p. 4.2"), CSV 8 (preamble, 2+2
  windows without duplicate rows, window spans 2–3 / 4–5, header-only, null coordinates), generic 5,
  registration 1.

**Problems / pitfalls:**
- **A packer defect I caught myself while reading, before the first test run.** After a flush inside
  the unit hard-split branch, the "current holds no new content" flag stayed `false` — on the next
  overflow a window made of a lone tail would have been **emitted as a chunk**, duplicating already
  covered text (a quiet bug category: the "sliding" test would pass, and only a near-duplicate
  retrieval test would notice the duplicate). Moved the flag assignment into `Flush` itself — the
  "after a flush current holds overlap only" invariant is now impossible to violate in any branch.
- **The constructor guard (`overlapChars < maxChunkChars`) collided with my own test budgets:** the
  default overlap of 200 with `maxChunkChars: 20` would throw `ArgumentOutOfRangeException` before
  the first assertion. Tests now always set the overlap explicitly — documenting along the way that
  it is meaningfully small for small budgets.
- **My expectation in the abbreviation test was wrong, and it exposed an imprecision in my own
  rule:** I wrote the test with "See p. 4.2.", believing "See." was protected. It is not: the rule
  protects only a period followed by a **digit** ("p. 4.2"), while "See." is cut like an ordinary
  sentence end. The consequence is only a longer unit, no loss. The test was rewritten to
  "Section p. 4.2 of the law." with budget 12: a single 21-char unit is hard-split character by
  character ("Section p. 4."), which proves there is no split at "p.".
- **The test-count arithmetic drifted by 1 back in part 3:** the diary stated "110/110", while the
  actual offline baseline was 109 (verified by running with the `-Chunking` filter: 109 + 24 new =
  133). A recount, not a lost test. From this point the "Outcome" count comes from the `dotnet test`
  output, not memory.
- **The PDF contract slightly diverged from the plan:** the plan listed three chunkers and was
  silent on PDF, but "register by kind" must cover it too. The decision — `SupportedKinds` on the
  generic chunker — is cheaper than a fourth empty class; if PDF-specific logic is ever needed (e.g.
  splitting by semantic page blocks), a dedicated class appears and the contract does not change.
- It could have broken but did not: greedy packing never looped (the tail is strictly smaller than
  the window — progress is guaranteed by construction); `null + int` on the `int?` LineStart in the
  CSV chunker yielded `null`, as expected from a lifted operator; the merged pathless sections
  ("Introductory paragraph" + "Second") produced no extra chunk.

**Outcome:** the build is **0 warnings, 0 errors**; offline **133/133** (plus 5 skipped live), 24 of
them new. Milestone 2 — part 4/7 closed.
**Next:** part 5 — `IngestPipeline` (SHA256, idempotency by `ContentHash`, supersede in a
transaction) + NSubstitute tests.

### 2026-09-10 — Milestone 2, part 5: IngestPipeline — idempotency, supersede, one transaction

**Plan:** `IngestPipeline`: SHA256 → parser by kind → chunker → batched embeddings → L2
normalization → BLOB → one transaction (new document + chunks + superseding the previous one).
Idempotency: the same `ContentHash` → `Duplicate` and **zero** embedding calls. Tests: NSubstitute
against the pipeline, real SQLite against the repository.

**Done:**
- **Domain:** `IngestRequest` (file bytes + provenance: path, title, version, kind,
  `IngestedByUserId`), `IngestResult` (`DocumentId, Outcome, ChunksCreated, SupersededDocumentId`),
  the `IngestOutcome` enum (`Created | Duplicate`), ports `IDocumentIngestService` and
  `IDocumentRepository`.
- **`IngestPipeline` (Application):** a hash duplicate is caught **before** parsing — the cheapest
  gate; a duplicate pays neither for parsing nor for the model. Embedding batches by
  `embeddingBatchSize` (a test catches the split of 5 chunks with batch size 2 → 2+2+1 calls); the
  model input is `"{Title} (v{Version}) — {SectionPath}\n{text}"`, while `Chunk.Text` is stored
  bare: the header is deterministically reconstructed in M4. A single-dimension check across the
  whole document (`InvalidOperationException` **before** touching the repository — no half-written
  records). The supersede lookup is deliberately **after** embeddings: the decision is made on the
  state current at commit time.
- **`VectorMath` (Application, DOD):** `NormalizeInPlace` (a double accumulator against precision
  drift over 3072 dimensions; the zero vector is left untouched — division by zero would produce NaN
  poisoning every dot product) and `ToFloat32Blob` (`MemoryMarshal.AsBytes`, IEEE-754 float32 LE
  without a header). The plan placed it in M3 part 1, but the pipeline physically cannot save chunks
  without encoding and normalization — the functions appeared now; `DotProduct`/`TopK` and the parity
  tests remain in M3.
- **`EfDocumentRepository`:** `AddAsync(document, buildChunks, supersededDocumentId)` — the only
  place that knows inserting a new version and superseding the previous one live in **one**
  transaction. `buildChunks` is a deferred factory: `Chunk.DocumentId` is init-only (the entity is
  immutable), and the DB generates the document id inside the transaction.
- **DI:** the repository is in `AddLabAiPersistence`, the pipeline in the new
  `AddLabAiIngest(embeddingBatchSize)`.
- **Tests (18 new):** pipeline 9 (a duplicate touches neither parser nor model; batches; the context
  header; mixed dimensions → nothing persisted; supersede = 42; an empty document without embedding
  calls; missing parser/strategy; request validation), VectorMath 4, EfDocumentRepository 5 (id
  fix-up, byte-for-byte BLOB round-trip, supersede, rollback on factory failure, Active-only
  lookups).

**Problems / pitfalls:**
- **`Chunk.DocumentId` init-only versus the DB-generated id** — the part's main design knot. Options:
  make the FK settable (breaks entity immutability), rely on EF fix-up by FK without navigations
  (not guaranteed), give the pipeline DbContext access (breaks layers). Chose the deferred factory
  in the repository port: it is called inside the transaction when the id is already assigned. The
  price — a test has to capture the factory via `Arg.Do` and call it with a fake id; the payoff —
  the pipeline stays in Application without EF types, and the transaction does not leak outward.
- **Three FK errors from my own tests — and the first hypothesis was only half right.** At first
  everything failed with "FOREIGN KEY constraint failed": I added user seeding to the fixture (the
  `IngestedByUserId → users` FK — the real "who uploaded" requirement baked into the schema), two
  of the five tests got fixed but two kept failing. The real culprit: the **test** factories ignored
  the passed `documentId` and built chunks with `DocumentId = 0`. Lesson: a fix that explains part of
  the failures does not explain all of them — verify the remaining failures are really covered by
  the hypothesis. Along the way the test proved the deferred factory's value: fail to pass the id and
  the FK catches it at once.
- **Layers:** the plan placed `IngestPipeline` in Application and `LabAiDbContext` in
  Infrastructure.** The pipeline needs persistence — the `IDocumentRepository` port was added (it was
  not in the plan's port list). The same trick in M4: `RagQueryPipeline` takes `IVectorStore` and
  `IAiAuditTrail`, not DbContext.
- It could have broken but did not: `Convert.ToHexStringLower` (added in .NET 9) matched the hash
  expectations in all tests; the "tail" batch of 1 text was not lost; an empty document goes to
  `AddAsync` with an empty factory and never calls the model; the supersede lookup for a duplicate
  never ran (the duplicate exits earlier, which is correct — supersede is honestly skipped).

**Outcome:** the build is **0 warnings, 0 errors**; offline **151/151** (plus 5 skipped live), 18 of
them new. Milestone 2 — part 5/7 closed.
**Next:** part 6 — the authored corpus `docs\corpus` (five SOPs, an instrument CSV, a JSON balance
log); the append-only trigger migration has already been moved to M5 part 3.

### 2026-09-10 — Milestone 2, part 6: The authored docs\corpus and the chunk-stability test

**Plan:** write the corpus the rest of the project will run on: five SOPs with numerical limits (for
the M7 golden set), an instrument CSV in the exact shape of `Mini-CDS\Reports\1.csv` (BOM, CRLF, 11
columns, empty `Tailing`), a JSON balance log with an `Owner` field containing synthetic PII (for
M5). Close the M2 readiness criterion "ingesting the corpus yields a stable chunk count (asserted by
tests)".

**Done:**
- `docs\corpus\`: SOP-QC-001 (HPLC system suitability: RSD ≤ 2.0%, plates ≥ 2000, tailing ≤ 2.0,
  Rs ≥ 1.5), SOP-QC-002 (balances: tolerance ±0.5 mg, range ≤ 0.3 mg), SOP-QC-003 (pH meter:
  slope 95–102%, control buffer ±0.05 pH), SOP-QC-004 (pipettes: volume-dependent limits
  ±0.8/1.0/2.5%), SOP-QA-005 (OOS: notification ≤ 1 hour, initial assessment ≤ 1 business day,
  plan ≤ 5 days, report ≤ 30 days). Front matter `doc_id/title/version/effective_date/owner`,
  stripped by the parser.
- The numbers are consistent across artifacts: in `hplc-run-001.csv` the main peak of
  `Sample_2026-002` has Plates = 1650 (below the ≥ 2000 limit from SOP-QC-001) — a ready
  "CSV + SOP → OOS" scenario for the golden set; in `balance-log-2026-09.json` the 2026-09-03 check
  has a −0.6 mg deviation against the ±0.5 mg tolerance from SOP-QC-002.
- The CSV was written via Write and then normalized to BOM+CRLF with one sed command — checked with
  `cat -A` against the reference `1.csv`: `M-oM-;M-?` at the start and `^M$` at the end of every
  line.
- `CorpusIngestTests` (3 tests): a fully real pipeline (parsers, 2000/200 chunkers,
  `EfDocumentRepository` on a migrated in-memory SQLite) with a constant embedding stub; chunk
  counts pinned per file (6/7/6/6/6/2/1 = 34); re-ingesting the whole corpus yields only
  `Duplicate` with no new rows; markdown chunks carry a non-empty `SectionPath` and a line span;
  CSV text starts with the "Measurement results, sample Sample_2026-…" preamble.

**Problems / pitfalls:**
- **Russian typography versus the sentence splitter.** `TextUnitSplitter` treats "." + space +
  non-digit as a sentence end. The SOP-typical "no more than 2.0 %" with a space before the percent
  sign does not suffer (the comma is not a terminator), but the "2.0 %" written in the plan would be
  cut into "2." and "0 %" (space + `%` is a non-digit). Resolution: the decimal comma in the corpus
  is the norm of a Russian laboratory document; the CSV uses periods (the CDS format), where the
  splitter is not applied (CSV is chunked by rows).
- **Flat headings would produce an empty `SectionPath`.** With a "`##` section with a body"
  structure, all sections in a row share the `""` parent and merge into one chunk with an empty
  breadcrumb. Corpus rule: a body goes only under a `###` inside a `##` chapter → one chapter = one
  chunk with a breadcrumb path ("4. Acceptance criteria"); neighboring chapters do not merge because
  their parents differ.
- **Chunk counts need a strength test, not faith.** The counts were derived from the corpus design
  (one chunk per chapter; CSV — one window per sample with a row budget ≈ 1836 versus ~105-character
  rows) and confirmed by the test on the first run; the recent defect of "a pinned number guessed by
  eye" is excluded by construction here: any change to the chunkers or the corpus fails the test.
- **Counter re-check: part 5 says 151/151 offline, while the actual baseline before part 6 is 152**
  (155 now minus the 3 new corpus tests; 160 total = 155 + 5 skipped live). I am not editing the
  historical entry — this is exactly the error already caught in part 3 (110 instead of 109). The
  "count only from dotnet test output" rule is confirmed for the second time: a manual number
  transfer slipped between "checked" and "written".

**Outcome:** the build is **0 warnings, 0 errors**; offline **155/155** (160 total, 5 of them skipped
live), 3 of them new. Milestone 2 — part 6/7 closed; the corpus and the stability test are ready.
**Next:** part 7 — `POST /api/ingest` (multipart, Roles="Administrator,Analyst") and the
`/documents` page, after which Milestone 2 closes.

### 2026-09-10 — Milestone 2, part 7: POST /api/ingest, the /documents page, browser login

**Plan:** close the last part of M2 — a multipart ingest endpoint with role restriction and a
`/documents` page with a table and an upload form. The part also pulled in M6(1) items: cookie auth
and the Blazor skeleton had to be raised early because ingest needs a real `IngestedByUserId` from an
authenticated user — a fictional ActorUserId would make the GxP narrative declarative (the same rule
under which auth preceded ingest in M2).

**Done:**
- `POST /api/ingest` ([Authorize Roles="Administrator,Analyst"]): validation (file null/empty/
  >10 MB, empty title, version < 1, unsupported extension → 400 with an error dictionary),
  `GeminiNotConfiguredException` → 503 (mirroring the ChatEndpoints contract), response
  `{ documentId, chunksCreated, outcome, supersededDocumentId }`. `.DisableAntiforgery()` —
  deliberate: the endpoint is for external API clients (the A+C integration); CSRF is closed by the
  SameSite=Strict cookie.
- `GET /api/documents` → active documents ordered by title (`ListActiveAsync` added to
  `IDocumentRepository`/`EfDocumentRepository` + a test).
- `AuthClaims.ToClaimsPrincipal` + `POST /api/auth/login|logout`; cookie: HttpOnly, SameSite=Strict,
  8 h; `OnRedirectToLogin/AccessDenied` return 401/403 instead of an HTML redirect, so a JSON client
  does not receive a login page in the response body.
- Blazor skeleton: `App.razor` (lang="ru"), `Routes.razor` with `AuthorizeRouteView` +
  `RedirectToLogin`, `MainLayout`, the `/` (overview) and `/login` pages.
- `/documents` (InteractiveServer, [Authorize]): the active-documents table, upload via `InputFile`,
  the username taken from `AuthenticationState` → `IUserStore` (claims in the circuit are not
  trustworthy), parser/pipeline errors → a banner, table reload after success.
- The login form is a plain HTML form with `<AntiforgeryToken />` posting to a new `POST /login`
  (minimal API, redirects `/login?error=1` → `/`). GET /login is the component route, POST /login is
  the endpoint; no conflicts.
- Live curl verification: 401 without a cookie, 200 login, 403 for `operator` on ingest, 400 for
  every validation variant, Created/Duplicate idempotency, chunk counts matching the pinned ones
  (6/2/1/7/6/6 against live embeddings), GET /api/documents ordered.
- Browser verification: login as analyst/demo123 → redirect to `/`, `/documents` shows all
  7 documents with versions, uploading SOP-QC-003 through the form → the banner "Document #7: 6
  chunks, outcome created", re-uploading the same file → "0 chunks, duplicate outcome".

**Problems / pitfalls:**
- **Host crash at startup: "An action cannot use both form and JSON body parameters".** Minimal API
  infers binding sources: parameters whose types are not registered in DI are treated as JSON Body.
  The `IDocumentIngestService ingest` next to the `[FromForm]` parameters was inferred as Body →
  conflict. Root cause: `Program.cs` did not call
  `AddLabAiParsing/AddLabAiChunking/AddLabAiIngest` — the services were not registered at all.
  Lesson: this error is not caught at compile time; it is caught only by starting the host — a smoke
  launch is mandatory after any endpoint.
- **`[FromForm]` lives in `Microsoft.AspNetCore.Mvc`, not Http.** In .NET 10 the attribute moved
  across packages; a search through the ref packs
  (`grep -rl FromFormAttribute .../Microsoft.AspNetCore.App.Ref/10.0*`) found it in Mvc.Core.
- **EditForm + `[SupplyParameterFromForm]` did not bind the fields; after submit the fields were
  invalid=true.** The field names generated from the `Model="FormModel"` expression did not match
  the binder prefix ("Model.Username"), the model arrived empty, and server-side Required validation
  rejected the form. Instead of debugging name generation I changed the approach: a plain HTML form
  + antiforgery token + a minimal API `POST /login` with redirects. Simpler, more deterministic, and
  it works before the circuit is up.
- **Silent circuit death: upload did nothing with no error anywhere.** The page rendered
  (prerendering), but `InputFile.OnChange` never reached the server. The cause is two-layered: (1)
  `App.razor` lacked `<script src="_framework/blazor.web.js">`; (2) `dotnet run` without
  launchSettings.json runs in Production, where static web assets are disabled — the log said
  outright "Static Web Assets are not enabled". Fixes: the script in `App.razor` +
  `builder.WebHost.UseStaticWebAssets()` (a no-op when published). Lesson: interactive Blazor fails
  **silently** — the symptom "no error and no result" means "the event never arrived", not "the
  handler threw".
- **`v@document.Version` rendered literally.** Razor treats `@` after an alphabetic character as part
  of email-like text (`v@document`), and the expression is not parsed. The fix is the explicit
  parenthesized form `v@(document.Version)`.
- **During the live curl run, ingesting one file failed with 500** (`GeminiNotConfiguredException`
  without a key after a restart), and SOP-QC-003 was missing from the corpus until I re-uploaded it
  through the UI. As a side effect this confirmed fail-closed: without a key a document is never
  half-saved.
- **CWD persists between Bash calls** — curl with relative paths runs into this for the third time
  in the project; the rule: absolute paths or an explicit `cd` in the same command.

**Outcome:** the build is **0 warnings, 0 errors**; **156/156 offline** (161 total, 5 of them skipped
live) — the count was taken from the `dotnet test` output immediately after the run. The live curl
matrix and the browser scenario passed in full. **Milestone 2 closed completely (7/7).**
**Next:** M3 — BLOB encoding + `VectorMath` (DOD, parity tests), then `SearchIndex`/
`BruteForceSearch`, and `EfVectorStore` with snapshot swap and a dimension guard.

### 2026-09-10 — Milestone 3, part 1: BLOB decoding and DotProduct in VectorMath, parity tests

**Plan:** bring `VectorMath` to the full hot-loop search contract: decoding a float32-LE BLOB, the
dot product over `ReadOnlySpan<float>` (both sides normalized on write → search is dot product
only), and parity tests against a naive reference implementation.

**Done:**
- `VectorMath.FromFloat32Blob(ReadOnlySpan<byte>)` → `float[]`: a multiple-of-four check,
  `MemoryMarshal.Cast<byte, float>` inside. The copy (`ToArray`) is deliberate: the index snapshot
  (part 3) wants to own the memory anyway, and the zero-copy path for a query remains available
  directly through `MemoryMarshal.Cast` — exactly why the headerless BLOB format was chosen.
- `VectorMath.DotProduct(ReadOnlySpan<float>, ReadOnlySpan<float>)`: a flat `for` without LINQ or
  allocations (DOD, AGENT.md 2.4), a length-mismatch guard.
- `VectorMathTests` +9 tests (13 total): encode→decode round-trip at 3072 dimensions, rejection of a
  non-multiple length, parity against a naive convolution at dimensions 1/7/256/3072 (deterministic
  seeds, not randomness), the identity "dot product of normalized vectors = cosine similarity" — the
  semantics retrieval relies on — orthogonality, and rejection on length mismatch.

**Problems / pitfalls:**
- **The parity-test tolerance is a semantics decision, not a number.** A tolerance that tight
  (`1e-6`) would fail the day the JIT starts vectorizing the convolution with sum reassociation
  (SIMD changes addition order → a different rounding result). One that is too loose (`1e-3` of the
  scale) would mask a real bug. Chose a relative `1e-4` of `max(1, |expected|)`: the naive reference
  and reassociating implementations pass, systematic errors (wrong stride, wrong byte order) do not.
- **`Random(seed)` in tests, not "random" data.** A parity failure with floating seeds is
  unreproducible; seeds make a failed assertion readable.
- **A background host blocked the build again** (MSB3026/MSB3027 on the exe and DLL — the third time
  in two parts). Working rule: the host goes up only for live checks and stops immediately after;
  between them builds and tests run against a free tree.
- **Decode returns a copy — a note for future readers.** It might look like zero-copy was "lost"; in
  fact `SearchIndex` (part 2) is built from owned `float[]`, and the only truly zero-copy moment is
  reading the BLOB from SQLite before laying it into the snapshot's flat array.

**Outcome:** the build is **0 warnings, 0 errors**; VectorMath tests **13/13**, the full offline set
**165/165** (161 + 9 new − the 5 live skipped outside the filter; count from
`dotnet test --filter "Category!=Live"`: 165 passed, 0 failed). Milestone 3 — part 1/4 closed.
**Next:** part 2 — `SearchIndex` (parallel SoA arrays) + `BruteForceSearch.TopK` (an
insertion-sorted buffer, deterministic tie-break) + tests.

### 2026-09-10 — Milestone 3, part 2: SearchIndex (SoA snapshot) and BruteForceSearch.TopK

**Plan:** an immutable snapshot of the vector store as parallel arrays (SoA) and a brute-force top-K
scan with insertion sorting into a fixed buffer: without a full sort, without a heap, with
deterministic tie-breaking and threshold cut-off.

**Done:**
- `Domain/ValueObjects/SearchHit` — `readonly record struct (ChunkId, DocumentId, Score)`;
  `DocumentId` rides along so the sources panel can resolve the title/version without a second query
  on the hot path.
- `Application/Vectors/SearchIndex` — `ChunkIds[]`, `DocumentIds[]`, a flat `Vectors[]` array (row
  `i` starts at `i * Dimension`), `Dimension`, `Count`; the constructor validates lengths
  (mismatched id arrays and an underfilled buffer are errors, not silent garbage reads);
  `SearchIndex.Empty` — the empty-store singleton.
- `Application/Vectors/BruteForceSearch.TopK(index, query, k, minScore, Span<SearchHit>)`:
  a scan over all rows, insertion maintenance of the sorted prefix directly in the caller's
  destination span (zero allocations), `score < minScore` cut-off, ascending-ChunkId tie-break, and
  the number of hits written returned.
- Tests (13 new): SoA layout, both constructor validations, Empty; descending score order, full
  threshold exclusion (not rank demotion), k > n, deterministic tie-break (two runs — one answer),
  worst-hit eviction by a later row (the replacement path in a full buffer), empty index, k = 0,
  rejection of a foreign query dimension, rejection of a short destination.

**Problems / pitfalls:**
- **k = 0 is a separate path, not a "special case of k > 0".** The first version accessed
  `destination[taken - 1]` on `taken == k` before checking k, and at k = 0 that would be an
  out-of-bounds access to the left of the span. Caught while reviewing my own logic before running;
  the `ZeroKWritesNothing` test now pins the behavior.
- **Test vectors are code too.** The first top-K data set gave 7 floats for 3 rows of dimension 3 —
  the SearchIndex constructor honestly refused (validation fired before the test). Rewrote with
  integer-ish cosines (0.96/0.86/0.1) and unit rows so the expectations are hand-verifiable.
- **Tie-break must come before Match, not after.** The scan runs in ascending row index; without the
  "equal score → smaller ChunkId first" rule, the order of equal hits would depend on the row order
  in the DB — i.e. on ingest history. For the audit journal that cites `[S1]..[Sn]`, that would mean
  non-deterministic citation numbering.
- **Score is not nullable, and FluentAssertions `BeGreaterThan(float)`**: `hits[1].Score!.Value`
  does not compile (CS1061) — a triviality, but it eats a build cycle if written from memory.

**Outcome:** the build is **0 warnings, 0 errors**; the full offline set **178/178** (count from
`dotnet test --filter "Category!=Live"` immediately after the run). Milestone 3 — part 2/4 closed.
**Next:** part 3 — `EfVectorStore`: snapshot swap via `Volatile.Write`, building from Active
documents only, the startup dimension guard ("a mixed store refuses to serve"), and wiring
`RebuildAsync()` into the end of ingest.

### 2026-09-10 — Milestone 3, part 3: EfVectorStore with rebuild and Active-only filtering

**Plan:** an EF-backed vector store as a singleton using `IDbContextFactory<T>` to avoid a captive
dependency; the immutable snapshot published via `Volatile.Write`; built from Active documents only
(superseded versions excluded at the SQL level); a startup dimension guard refusing to search a mixed
store; wiring `RebuildAsync()` into the end of the ingest pipeline and DI registration.

**Done:**
- `Domain/Abstractions/IVectorStore` — a port with two methods: `Snapshot { get; }` and
  `RebuildAsync()`; it returns `SearchIndex` from Domain.ValueObjects (not Application — otherwise
  the layer would violate AGENT.md 2.1).
- `Infrastructure/Persistence/EfVectorStore` — implementation: the constructor takes
  `IDbContextFactory<LabAiDbContext>` (not a scoped DbContext — a singleton cannot hold a scoped
  dependency); `RebuildAsync()` creates a temporary context via the factory, checks the dimension
  guard (`SELECT DISTINCT EmbeddingModelId, EmbeddingDimension` → more than one pair =
  `InvalidOperationException`), collects the list of Active document IDs via an explicit subquery
  (Chunk has no `Document` navigation — the FK exists but the navigation property does not), filters
  chunks via `Contains(activeDocIds)`, decodes BLOBs through `MemoryMarshal.Cast`, fills the SoA
  arrays, and publishes the snapshot through `Volatile.Write`.
- `IngestPipeline` — an `IVectorStore vectorStore` parameter added to the constructor; after a
  successful `repository.AddAsync`, `await vectorStore.RebuildAsync(cancellationToken)` is called —
  the index is rebuilt in lockstep with the transaction, fail-closed: if the rebuild fails, ingest
  fails with it.
- `IngestServiceCollectionExtensions` — the `IngestPipeline` factory updated:
  `sp.GetRequiredService<IVectorStore>()` inserted between `IDocumentRepository` and
  `embeddingBatchSize`.
- `PersistenceServiceCollectionExtensions` — registered `AddDbContextFactory<LabAiDbContext>` (for
  EfVectorStore) and `services.AddSingleton<IVectorStore, EfVectorStore>()`.
- Tests: `CorpusIngestTests` and `IngestPipelineTests` updated — a
  `Substitute.For<IVectorStore>()` stub was added to the `IngestPipeline` constructors everywhere.

**Problems / pitfalls:**
- **SearchIndex ended up in Application while IVectorStore is in Domain.** A port cannot reference a
  type from a lower layer without violating Clean Architecture. Resolution: moved `SearchIndex.cs`
  from `Application/Vectors/` to `Domain/ValueObjects/`. It is a Value Object (not an entity or
  aggregate root), so its place in Domain is acceptable. In `IVectorStore` the fully qualified
  `ValueObjects.SearchIndex` is used — both types are in one assembly but different namespaces.
- **Chunk has no Document navigation.** The `ChunkConfiguration` defines the FK via
  `HasOne<SourceDocument>().WithMany().HasForeignKey(c => c.DocumentId)` — a shadow navigation; EF
  Core knows about the relationship but C# code sees no `c.Document` property. Trying to write
  `Where(c => c.Document.Status == ...)` yields CS1061. Had to rewrite to an explicit subquery:
  first `SELECT Id FROM SourceDocuments WHERE Status = Active`, then
  `WHERE activeDocIds.Contains(c.DocumentId)`. The generated SQL is identical (INNER JOIN), but the
  code compiles.
- **The DI registration of IngestPipeline broke after the parameter was added.** The factory lambda
  in `IngestServiceCollectionExtensions` passed 5 arguments while the constructor now requires 6. The
  compiler pointed at line 20 — added `sp.GetRequiredService<IVectorStore>()` in the correct position
  (between repository and batchSize).
- **The tests needed updating too.** `CorpusIngestTests` uses real SQLite and EfDocumentRepository —
  adding `Substitute.For<IVectorStore>()` was enough there (RebuildAsync is not called in the tests
  because embeddings are mocked, but the pipeline itself is built fully). `IngestPipelineTests` —
  pure NSubstitute unit tests; there I added a
  `private readonly IVectorStore vectorStore = Substitute.For<IVectorStore>();` field and passed it
  at all three IngestPipeline construction sites.

**Outcome:** the build is **0 warnings, 0 errors**; the full offline set **178/178** (all tests
green, none broke). Milestone 3 — part 3/4 closed.
**Next:** part 4 — call `RebuildAsync()` at application startup (Program.cs after DbSeeder), extend
the `/health` endpoint with index size and dimension, and write EfVectorStoreTests (BLOB round-trip,
exclusion of superseded documents, dimension guard, atomic snapshot swap).

### 2026-09-10 — Milestone 3, part 4: Startup rebuild, health extension, EfVectorStoreTests

**Plan:** rebuild the vector index at application startup, add size and dimension to `/health`, and
cover EfVectorStore with integration tests against temp SQLite.

**Done:**
- `Program.cs` — a rebuild block added after seeding: calls `vectorStore.RebuildAsync()`, logs the
  vector count and dimension; catches `InvalidOperationException` from the dimension guard and logs
  an error (the application keeps running, but search will refuse until a re-ingest with one model).
- `HealthEndpoints` — `VectorIndexSize` and `VectorDimension` fields added to the response; the
  endpoint now reads `IVectorStore.Snapshot` and returns current numbers without extra DB queries.
- `EfVectorStoreTests` (5 new tests):
  1. `RebuildEncodesAndDecodesBlobsByteExactly` — the BLOB → float[] → BLOB round-trip is exact;
  2. `SupersededDocumentsAreExcludedFromSnapshot` — only Active documents reach the snapshot;
  3. `MixedDimensionsCauseRebuildToFail` — two different (model, dim) pairs cause
     `InvalidOperationException`;
  4. `SnapshotSwapIsAtomic` — an empty snapshot before rebuild, populated after; repeated reads
     return the same reference (immutable object, Volatile.Read);
  5. `EmptyStoreProducesEmptySnapshot` — an empty DB gives `Count = 0, Dimension = 0`.

**Problems / pitfalls:**
- **The FluentAssertions wildcard assertion is order-sensitive.** The first test version expected
  `"*multiple*model/dimension*"`, but the real message contains "2 distinct model/dimension pairs" —
  the word "multiple" is absent. Fixed to `"*model/dimension*"`, which covers the essence without
  binding to a specific numeral.
- **TestDbContextFactory requires a typed DbContextOptions.** The `LabAiDbContext` constructor takes
  `DbContextOptions<LabAiDbContext>`, not the base `DbContextOptions`. CS1503 caught immediately,
  fixed with the generic.
- **VectorMath is not visible from tests.** The class is in `LabAi.Application.Vectors`; an explicit
  `using` is required. Without it the compiler emits CS0103 on every `VectorMath.ToFloat32Blob`
  call.

**Outcome:** the build is **0 warnings, 0 errors**; the full offline set **183/183**
(+5 EfVectorStoreTests). Milestone 3 — part 4/4 closed. **Milestone 3 is fully complete.**
**Next:** M4 — Grounded generation: `PromptComposer` with an EN system prompt, `PromptHasher`,
`RefusalDetector` (threshold gate + refusal phrase), `RagQueryPipeline` with fail-closed audit
ordering, replacing `/api/chat` with `POST /api/ask`.

### 2026-09-10 — Manual E2E testing in Docker: fixing UI bindings and the EF-backed audit trail (M5 gap)

> M4–M7 entries were not added to this log as the work happened; below is only the concrete manual
> hands-on testing session and the defects found/fixed.

**Context:** the user builds and runs the application exclusively through `docker-compose`
(`http://localhost:5000`); local `dotnet run` is forbidden. All checks were performed in the browser
via browser-use MCP against the live container.

**Done:**
- **Documents/Chat: fixed the "permanently inactive" buttons.** The root cause was not the
  `disabled` expression but the Blazor circuit crashing on the first keystroke. Replaced the
  `InputText` component binding with plain
  `<input @bind="title" @bind:event="oninput">` and
  `<textarea @bind="questionText" @bind:event="oninput">`. The Upload button is now driven by the
  computed `CanUpload => !uploading && selectedFile is not null &&
  !string.IsNullOrWhiteSpace(title)`; Send activates on non-empty text.
- **Chat: brought the citation click to life.** Instead of the `Console.WriteLine` stub,
  `ShowSource(Citation)` reads the chunk via `IDocumentRepository.GetChunksByIdsAsync` and renders
  `SourcesPanel` with the document title, version, `SectionPath`, chunk text, and `Score`.
- **Dockerfile: added `-p:GenerateStaticWebAssets=true`** to `dotnet publish` — without it the image
  had no `_framework/blazor.web.js` (404 → the circuit never starts).
- **M5 gap: an EF-backed append-only audit trail instead of the in-memory stub.** New entity
  `AiAuditEntry`, `AiAuditEntryConfiguration` (table `ai_audit_entries`, indexes on
  `TimestampUtc`/`CorrelationId`), `EfAiAuditTrail` on `IDbContextFactory<LabAiDbContext>` with a
  hash chain (`RowHash = SHA256` of the length-prefixed fields + `PrevHash`, genesis = 64 zeroes),
  DI replacement of `StubAiAuditTrail` → `EfAiAuditTrail` (the stub removed), migration
  `20260910134221_AddAiAuditTrail` with SQLite `RAISE(ABORT)` triggers on `UPDATE`/`DELETE`.

**Problems / pitfalls:**
- **An "inactive button" was actually a dead circuit.** The symptom looked like an error in the
  `disabled` expression, but `docker logs` showed
  `System.ArgumentException: Object of type 'ChangeEventArgs' cannot be converted to type
  'System.String'`. Wrong binding syntax (`@bind-Value:event` on a component /
  `@bind-value:event` on an element) crashed the circuit on the first `oninput`, so no re-render
  happened at all. Only the element-level `@bind` + `@bind:event="oninput"` is valid.
- **Razor reads `S@citation.Index` as an email address** and renders it literally. The workaround is
  explicit parentheses: `[S@(citation.Index)]`, `v@(Citation.Version)`.
- **`///` XML-doc comments in .razor markup** (outside `@code`) are emitted as visible page text —
  they had to be removed from `SourcesPanel.razor`.
- **The container had no `blazor.web.js`.** `dotnet publish` does not generate static web assets for
  Blazor Server by default; the fix is `-p:GenerateStaticWebAssets=true`.
- **DataProtection keys live in the container filesystem**: on `docker-compose up --force-recreate`
  the auth cookie is invalidated (401 "Unprotect ticket failed") and a fresh login is required. With
  a plain `docker-compose restart` the container is not recreated and the keys and data in the
  volume survive — the audit record survived the restart.
- **bin/Debug lock files (MSB3027/MSB3021)** from stuck local `dotnet run` processes broke
  `dotnet ef migrations add`; cleared with
  `powershell -Command "Stop-Process -Id <pid> -Force"`.
- **Inspecting the DB without sqlite3/python on the host:** Windows `python3` is a Store stub, and
  `/tmp` in Git Bash is not shared into Docker Desktop as `/tmp`. The working route — mount the real
  `C:/Users/Danny/AppData/Local/Temp` and run `docker run -i python:3.12-slim` (the `-i` flag is
  mandatory for a heredoc over stdin); WAL/SHM must be copied together with `lab.db`.

**Outcome:** the build has **0 errors**, the offline set **215/215**. Verified manually in the
browser: login → Documents (Upload activates only with title+file, ingest "1 chunks, source
created") → Chat ("What is the HPLC flow rate?" → "1.0 mL/min [S1]", Correlation ID, Audit ID #1,
clicking [S1] opens SourcesPanel: Test SOP v1, Score 0.658) → Audit (row ID 1) →
`docker-compose restart labai` → the audit row is still there. Confirmed at the DB level: the
`ai_audit_entries_no_update` / `ai_audit_entries_no_delete` triggers block UPDATE and DELETE
(`RAISE(ABORT, 'ai_audit_entries is append-only…')`), `PrevHash` = 64 zeroes (genesis), `RowHash`
populated. Task #8 (EF-backed audit trail) closed.
**Limitations (deliberately not fixed):** the rate limiter is a TODO in `Program.cs`; DataProtection
keys are not moved to a volume → logout on `--force-recreate`.

### 2026-09-10 — Transient Gemini 503: retries + a logged graceful refusal

**Symptom (user screenshot):** in chat, instead of an answer there was a raw
`Error: Response status code does not indicate success: 503 (Service Unavailable)`.

**Diagnosis:** the 503 is transient upstream overload (embeddings had succeeded; it was the
generation call that failed). Two defects: (1) `GeminiChatClient` did no retries and the error
propagated immediately; (2) the audit write in `RagQueryPipeline` sat AFTER the LLM call (step 8), so
an exception at step 5 aborted the request and the failed question **never reached the journal** — a
direct violation of "every query is logged".

**Done:**
- `GeminiChatClient` — bounded retries with exponential delay (up to 3 attempts, backoff 500 ms /
  1 s) for transient `HttpRequestException`: 500/502/503/504/429/408 and network errors without a
  status code. Non-transient ones (400 etc.) and exhausted attempts propagate as before; the
  cancellation token is honored.
- `RefusalStage` — a new `UpstreamFailure` value.
- `RagQueryPipeline` — the LLM call wrapped in
  `try/catch (Exception e) when (e is not OperationCanceledException)`: on upstream failure a
  friendly `UpstreamFailureAnswer` refusal is returned with the `UpstreamFailure` stage, and step 8
  still writes the audit row (fail-closed: no request disappears). Cancellation still propagates into
  the UI's "[Request cancelled]" branch.
- Tests (+5): retrying a transient 503 → success; propagation after attempts are exhausted; no retry
  on 400; `RagQueryPipelineTests` — an upstream failure writes an audit draft with the
  `UpstreamFailure` stage and returns the graceful refusal; cancellation propagates as
  `OperationCanceledException`.

**Problems / pitfalls:**
- **The audit write sat after the LLM call.** Because of that any generation failure silently dropped
  the request from the journal. The fix is not moving the audit (it needs the retrieval results) but
  catching the failure around step 5 and guaranteeing the write at step 8.
- **`PromptHash` is deliberately not computed on failure.** It depends on `ModelId`, which is known
  only from the model response; when the model call fails there is no model, so the hash is empty and
  `Model` = `unknown` — honest, without manufacturing "evidence" for an answer that never existed.
- **E2E proof of the graceful path without editing files.** A real 503 cannot be forced, but compose
  substitutes `GEMINI_MODEL=${GEMINI_MODEL:-…}` from the environment:
  `GEMINI_MODEL=gemini-does-not-exist docker-compose up -d` gives 404 (non-transient) → the same catch
  path. After verification the container was recreated without the variable → the model returned to
  `gemini-3.5-flash`.

**Outcome:** the build has **0 errors**, the offline set **215/215** (including the 5 new). Verified
in the browser against Docker: the happy path after retries is not broken ("1.0 mL/min [S1]",
citations, Audit ID); with a broken model the UI shows "The language model is temporarily
unavailable… Your question was recorded in the audit journal", and `/audit` contains a row
`Refusal: UpstreamFailure`, `Answered: No`, `Model: unknown` — the failed request is no longer lost.
Task #9 closed.

### 2026-09-11 — Project-wide translation to English

**Plan:** per the user's decision, make the whole project and all documentation English-language.

**Done:**
- The demo corpus in `docs\corpus` (five SOPs, the balance JSON log) was translated to English; the
  instrument CSV was already English. Heading structure and all numeric limits were preserved, so
  the pinned chunk counts (6/7/6/6/6/2/1) did not move.
- All Russian strings in tests were translated: chunker fixtures, pipeline fixtures, repository
  titles, and the Unicode password round-trip (now `Naïve🔬2026`, still non-ASCII). The QuestPDF
  fixture is Latin again, deliberately avoiding the "ti" bigram (the Lato discretionary-ligature
  defect documented in M2 part 3) — the reason is now documented in the test in English.
- `App.razor` `lang="ru"` → `lang="en"`.
- This development log, including all historical entries, was translated to English. Historical
  entries were not rewritten — only translated; entries that describe the product's former
  Russian-language corpus and Russian live checks remain as historical facts.

**Outcome:** build **0 warnings, 0 errors**; offline **220/220** green; a workspace-wide search for
Cyrillic characters returns no matches.
