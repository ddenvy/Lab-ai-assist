using LabAi.Application.Rag;
using LabAi.Domain.Abstractions;
using LabAi.Infrastructure.Ai;
using LabAi.Infrastructure.Chunking;
using LabAi.Infrastructure.Ingest;
using LabAi.Infrastructure.Parsing;
using LabAi.Infrastructure.Persistence;
using LabAi.Web.Components;
using LabAi.Web.Endpoints;
using LabAi.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

// DotNetEnv must run before the builder: CreateBuilder snapshots the process environment into
// IConfiguration once, so variables loaded afterwards would be invisible to configuration binding.
DotNetEnv.Env.TraversePath().Load();

// Bootstrap logger covers the window between process start and the host taking over Serilog, so a
// failure inside CreateBuilder/Build is still written somewhere.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Non-published runs (dotnet run) default to Production without launchSettings.json, where
    // Blazor framework scripts (_framework/blazor.web.js) are otherwise not served and the
    // interactive circuit silently never connects. A no-op for published output.
    builder.WebHost.UseStaticWebAssets();

    builder.Host.UseSerilog(static (context, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext());

    var connectionString = builder.Configuration.GetConnectionString("LabDb")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:LabDb is not configured. Nothing can be retrieved or audited without a database.");

    // SQLite cannot create the parent directory, and "unable to open database file" says nothing about
    // which path was missing. Derived from the connection string so an absolute path works unchanged.
    var databaseDirectory = Path.GetDirectoryName(new SqliteConnectionStringBuilder(connectionString).DataSource);
    if (!string.IsNullOrEmpty(databaseDirectory))
        Directory.CreateDirectory(databaseDirectory);

    builder.Services.AddLabAiPersistence(connectionString);
    builder.Services.AddLabAiParsing();
    builder.Services.AddLabAiChunking(
        builder.Configuration.GetValue("Rag:Chunking:MaxChunkChars", 2000),
        builder.Configuration.GetValue("Rag:Chunking:OverlapChars", 200));
    builder.Services.AddLabAiIngest(
        builder.Configuration.GetValue("Rag:EmbeddingBatchSize", 16));

    // The cookie is the single auth scheme by design: a Blazor Server circuit is a WebSocket that
    // cannot carry an Authorization header, so one mechanism must cover JSON endpoints, SSR pages
    // and the interactive circuit. SameSite=Strict is the cross-site POST defence that lets the
    // ingest endpoint stay token-free for external API clients (Mini-CDS).
    builder.Services
        .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/login";
            options.AccessDeniedPath = "/login";
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.Cookie.Name = "LabAi.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;

            // JSON API endpoints must receive a status code, not a login-page redirect: a client
            // that never renders HTML would otherwise turn 401 into a confusing HTML body.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

    builder.Services.AddAuthorization();
    builder.Services.AddCascadingAuthenticationState();

    // Rate limiter: 10 requests per minute per authenticated user (deferred to M5 due to .NET 10 API changes).
    // TODO: Re-enable with correct .NET 10 syntax.

    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    var gemini = GeminiOptionsFactory.Create(builder.Configuration);
    builder.Services.AddSingleton(gemini);
    builder.Services.AddLabAiGemini(gemini);

    // RAG pipeline services (M4).
    var ragConfig = builder.Configuration.GetSection("Rag");

    // PII masking for audit trail and prompt composition.
    builder.Services.AddSingleton<IPiiMasker, LabAi.Application.Security.RegexPiiMasker>();

    builder.Services.AddSingleton<IRagQueryService>(sp => new LabAi.Application.Rag.RagQueryPipeline(
        sp.GetRequiredService<IEmbeddingService>(),
        sp.GetRequiredService<IVectorStore>(),
        sp.GetRequiredService<IGroundedChatClient>(),
        sp.GetRequiredService<IAiAuditTrail>(),
        sp.GetRequiredService<IDocumentRepository>(),
        sp.GetRequiredService<IPiiMasker>(),
        ragConfig.GetValue("TopK", 5),
        ragConfig.GetValue("MinScore", 0.35)));

    // AI audit trail — stub for M4, real implementation in M5.
    builder.Services.AddSingleton<IAiAuditTrail, StubAiAuditTrail>();

    if (!gemini.IsConfigured)
    {
        // Deliberately a warning, not fail-fast. The golden-set and E2E suites must be able to host
        // the application with no key at all; the Gemini adapters throw a descriptive exception on
        // first use instead, and /health reports the missing key.
        Log.Warning(
            "GEMINI_API_KEY is not set; AI endpoints will fail on first use. " +
            "Copy .env.example to .env and fill in the key.");
    }

    var app = builder.Build();

    // Fail-fast here, unlike the missing Gemini key: without a database there is no corpus to search
    // and no journal to write, so the product's central promise is unachievable and starting would
    // only produce answers that cannot be evidenced.
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<LabAiDbContext>();
        await db.Database.MigrateAsync();

        // WAL is persistent in the database file, so one statement at startup is enough. It cannot go
        // in a migration: migrations run inside a transaction, and journal_mode is a no-op there.
        // Without it a writer blocks every reader, and ingest would freeze the audit page.
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");

        var seeded = await scope.ServiceProvider.GetRequiredService<DbSeeder>()
            .SeedAsync(builder.Configuration["Demo:Password"]);

        Log.Information("Database ready; seeded {CreatedCount} account(s)", seeded.Created.Count);

        foreach (var user in seeded.Created.Where(u => u.Password is not null))
        {
            // Console only, deliberately bypassing Serilog: a generated credential must never land in
            // logs/labai-*.log. Unreachable while Demo:Password is configured, which is the normal case.
            Console.WriteLine($"Generated password for '{user.Username}': {user.Password}");
        }

        // Rebuild vector index on startup so /health reports size and dimension even before any ingest.
        var vectorStore = scope.ServiceProvider.GetRequiredService<LabAi.Domain.Abstractions.IVectorStore>();
        try
        {
            await vectorStore.RebuildAsync();
            var snapshot = vectorStore.Snapshot;
            Log.Information(
                "Vector index rebuilt: {Count} vectors, dimension {Dimension}",
                snapshot.Count,
                snapshot.Dimension);
        }
        catch (InvalidOperationException ex)
        {
            // Dimension guard fired: mixed models/dimensions in the store. The app still starts so that
            // non-AI routes work, but search will refuse to serve until re-ingest with one model.
            Log.Error(ex, "Vector index contains embeddings from multiple model/dimension pairs. Search disabled.");
        }
    }

    // First in the pipeline so every downstream log line carries the id.
    app.UseMiddleware<CorrelationIdMiddleware>();

    // Registered inside the correlation scope on purpose: the middleware disposes its LogContext
    // property when the downstream pipeline returns, so placing this first would leave the
    // "Request finished" line without a CorrelationId.
    app.UseSerilogRequestLogging();

    app.UseAuthentication();
    app.UseAuthorization();

    // Blazor SSR form posts (login) carry antiforgery tokens; without the middleware they are
    // rejected. Must sit after authentication so the token validation sees the full context.
    app.UseAntiforgery();

    app.MapStaticAssets();
    app.MapHealthEndpoints();

    // Rate limiter: deferred to M5 due to .NET 10 API changes.
    // app.UseRateLimiter();

    // Temporary smoke endpoint for the Gemini wiring; replaced by POST /api/ask in Milestone 4.
    app.MapChatEndpoints();
    app.MapAuthEndpoints();
    app.MapIngestEndpoints();
    app.MapAskEndpoints();

    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    Log.Information("LabAi host starting");
    app.Run();
}
catch (Exception exception)
{
    Log.Fatal(exception, "LabAi host terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
