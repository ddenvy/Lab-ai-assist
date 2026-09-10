using LabAi.Infrastructure.Ai;
using LabAi.Web.Endpoints;
using LabAi.Web.Infrastructure;
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

    var gemini = GeminiOptionsFactory.Create(builder.Configuration);
    builder.Services.AddSingleton(gemini);

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

    // First in the pipeline so every downstream log line carries the id.
    app.UseMiddleware<CorrelationIdMiddleware>();

    // Registered inside the correlation scope on purpose: the middleware disposes its LogContext
    // property when the downstream pipeline returns, so placing this first would leave the
    // "Request finished" line without a CorrelationId.
    app.UseSerilogRequestLogging();

    app.MapGet("/", static () => Results.Text("Lab AI Assistant", "text/plain; charset=utf-8"));
    app.MapHealthEndpoints();

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
