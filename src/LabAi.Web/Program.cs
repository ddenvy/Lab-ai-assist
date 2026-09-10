var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/", () => Results.Text("Lab AI Assistant", "text/plain; charset=utf-8"));

app.Run();
