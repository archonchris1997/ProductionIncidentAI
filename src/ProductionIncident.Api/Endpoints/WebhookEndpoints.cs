using System.Text.Json.Serialization;
using ProductionIncident.Application.Incidents;
using ProductionIncident.Application.Workflows;

namespace ProductionIncident.Api.Endpoints;

/// <summary>Event gateway: Azure Monitor alerts (common alert schema) → incidents.</summary>
public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/azure-monitor", async (AzureMonitorAlert alert, IncidentWorkflow workflow, CancellationToken ct) =>
        {
            var e = alert.Data?.Essentials;
            if (e is null)
            {
                return Results.BadRequest(new { error = "Not a common alert schema payload." });
            }

            if (!string.Equals(e.MonitorCondition, "Fired", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Ok(new { ignored = $"monitorCondition={e.MonitorCondition}" });
            }

            var alertKey = e.AlertId?.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? Guid.NewGuid().ToString("N");
            var request = new IncidentRequest
            {
                IncidentId = $"AM-{alertKey[..Math.Min(alertKey.Length, 40)]}",
                Title = string.IsNullOrWhiteSpace(e.AlertRule) ? "Azure Monitor alert" : e.AlertRule,
                Description = e.Description ?? "",
                Service = e.ConfigurationItems?.FirstOrDefault()?.ToLowerInvariant(),
                Severity = e.Severity switch
                {
                    "Sev0" or "Sev1" => "SEV1",
                    "Sev2" => "SEV2",
                    "Sev3" => "SEV3",
                    _ => "SEV4",
                },
                Source = "azure-monitor",
            };

            var errors = IncidentEndpoints.Validate(request);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var state = await workflow.StartAsync(request, ct);
            return Results.Accepted($"/incidents/{state.IncidentId}", new { state.IncidentId, state.Phase });
        })
        .WithTags("Webhooks")
        .WithSummary("Azure Monitor action group webhook (common alert schema).");

        return app;
    }

    public sealed record AzureMonitorAlert(
        [property: JsonPropertyName("schemaId")] string? SchemaId,
        [property: JsonPropertyName("data")] AzureMonitorData? Data);

    public sealed record AzureMonitorData(
        [property: JsonPropertyName("essentials")] AzureMonitorEssentials? Essentials);

    public sealed record AzureMonitorEssentials(
        [property: JsonPropertyName("alertId")] string? AlertId,
        [property: JsonPropertyName("alertRule")] string? AlertRule,
        [property: JsonPropertyName("severity")] string? Severity,
        [property: JsonPropertyName("monitorCondition")] string? MonitorCondition,
        [property: JsonPropertyName("configurationItems")] IReadOnlyList<string>? ConfigurationItems,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("firedDateTime")] DateTimeOffset? FiredDateTime);
}
