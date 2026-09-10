using LabAi.Infrastructure.Ai;
using LabAi.Infrastructure.Persistence;
using LabAi.Web.Endpoints;
using LabAi.Web.Infrastructure;
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

    var gemini = GeminiOptionsFactory.Create(builder.Configuration);
    builder.Services.AddSingleton(gemini);
    builder.Services.AddLabAiGemini(gemini);

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
    }

    // First in the pipeline so every downstream log line carries the id.
    app.UseMiddleware<CorrelationIdMiddleware>();

    // Registered inside the correlation scope on purpose: the middleware disposes its LogContext
    // property when the downstream pipeline returns, so placing this first would leave the
    // "Request finished" line without a CorrelationId.
    app.UseSerilogRequestLogging();

    app.MapGet("/", static () => Results.Text("Lab AI Assistant", "text/plain; charset=utf-8"));
    app.MapHealthEndpoints();

    // Temporary smoke endpoint for the Gemini wiring; replaced by POST /api/ask in Milestone 4.
    app.MapChatEndpoints();

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
