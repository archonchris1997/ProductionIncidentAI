using System.Text.Json.Serialization;
using ProductionIncident.Api.Endpoints;
using ProductionIncident.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProductionIncidentSystem(builder.Configuration);
builder.Services.AddIncidentObservability(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/", (IConfiguration config) => Results.Ok(new
{
    service = "production-incident-ai",
    aiProvider = config["AI:Provider"] ?? "Scripted",
    toolMode = config["Mcp:Mode"] ?? "Local",
    endpoints = new[]
    {
        "POST /incidents", "GET /incidents", "GET /incidents/{id}", "GET /incidents/{id}/report", "GET /incidents/{id}/receipts",
        "POST /incidents/{id}/stop", "GET /approvals", "POST /approvals/{id}/decision", "POST /webhooks/azure-monitor",
        "GET /tools", "GET /skills", "GET /knowledge/search?q=", "GET /memory/episodes", "GET /memory/facts",
    },
}));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapIncidentEndpoints();
app.MapApprovalEndpoints();
app.MapWebhookEndpoints();
app.MapOperationsEndpoints();

app.Run();

/// <summary>Entry point (public for WebApplicationFactory in integration tests).</summary>
public partial class Program
{
}
