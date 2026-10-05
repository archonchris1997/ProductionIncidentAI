using ProductionIncident.Mcp;
using ProductionIncident.Mcp.Tools;

// MCP server exposing the production tools over Streamable HTTP.
//
// Security principle 3: read-only investigation tools and write/remediation tools are served by
// SEPARATE processes with separate identities. Run two instances:
//   McpServer__Profile=investigation  → logs, database, metrics, deployment (read-only)
//   McpServer__Profile=operations     → rollback, restart, scale, config... (production-changing)
// Agents only ever connect to the investigation server; the ActionExecutor (after human approval)
// is the only client of the operations server.
//   McpServer__Profile=all            → both (local development only: the fake backends keep their
//                                       state per process, so verification sees the effect of an action)

var builder = WebApplication.CreateBuilder(args);

var profile = builder.Configuration["McpServer:Profile"] ?? "investigation";
var apiKey = builder.Configuration["McpServer:ApiKey"];

builder.Services.AddFakeProductionBackends(); // replace with real backends (Log Analytics, Azure Monitor, ...)

var mcp = builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true);

var serveOperations = profile.Equals("operations", StringComparison.OrdinalIgnoreCase) || profile.Equals("all", StringComparison.OrdinalIgnoreCase);
var serveInvestigation = !profile.Equals("operations", StringComparison.OrdinalIgnoreCase);

if (serveInvestigation)
{
    mcp.WithTools<LogsTools>()
       .WithTools<DatabaseTools>()
       .WithTools<MetricsTools>()
       .WithTools<DeploymentTools>();
}

if (serveOperations)
{
    mcp.WithTools<OperationsTools>();
}

var app = builder.Build();

if (!string.IsNullOrEmpty(apiKey))
{
    // Minimal shared-secret auth for local/test environments. Production: Entra ID (managed identity) + least privilege.
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/mcp") &&
            !string.Equals(context.Request.Headers["X-Api-Key"], apiKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    });
}

app.MapGet("/", () => Results.Ok(new { server = "production-incident-mcp", profile }));
app.MapMcp("/mcp");

app.Run();
