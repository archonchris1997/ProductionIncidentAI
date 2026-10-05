using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ProductionIncident.IntegrationTests;

/// <summary>
/// Full HTTP flow with the real host (background worker, scripted model, in-process tools):
/// report → investigate → approval → action → verification → report.
/// </summary>
public sealed class ApiEndToEndTests(ApiEndToEndTests.Factory factory) : IClassFixture<ApiEndToEndTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Persistence:Provider", "InMemory");
            builder.UseSetting("AI:Provider", "Scripted");
            builder.UseSetting("Mcp:Mode", "Local");
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<JsonElement> WaitForPhaseAsync(HttpClient client, string incidentId, params string[] phases)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/incidents/{incidentId}", Ct);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var state = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
                if (phases.Contains(state.GetProperty("phase").GetString()))
                {
                    return state;
                }
            }

            await Task.Delay(100, Ct);
        }

        throw new TimeoutException($"{incidentId} did not reach [{string.Join(", ", phases)}].");
    }

    [Fact]
    public async Task Incident_is_investigated_approved_executed_and_reported_over_http()
    {
        var client = factory.CreateClient();

        var create = await client.PostAsJsonAsync("/incidents", new
        {
            incidentId = "INC-API-1",
            title = "checkout-api HTTP 500 after deployment",
            service = "checkout-api",
            severity = "SEV1",
        }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, create.StatusCode);

        var waiting = await WaitForPhaseAsync(client, "INC-API-1", "AwaitingApproval", "Escalated", "Failed");
        Assert.Equal("AwaitingApproval", waiting.GetProperty("phase").GetString());

        var approvals = await client.GetFromJsonAsync<JsonElement>("/approvals?incidentId=INC-API-1&status=Pending", Ct);
        var approval = Assert.Single(approvals.EnumerateArray());
        Assert.Equal("rollback_deployment", approval.GetProperty("action").GetProperty("tool").GetString());

        var decision = await client.PostAsJsonAsync($"/approvals/{approval.GetProperty("id").GetString()}/decision",
            new { approved = true, decidedBy = "oncall@example.com", comment = "go" }, Ct);
        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);

        var resolved = await WaitForPhaseAsync(client, "INC-API-1", "Resolved", "Escalated", "Failed");
        Assert.Equal("Resolved", resolved.GetProperty("phase").GetString());
        Assert.True(resolved.GetProperty("verified").GetBoolean());

        var report = await client.GetStringAsync("/incidents/INC-API-1/report", Ct);
        Assert.Contains("## Root cause", report);
        Assert.Contains("rollback_deployment", report);

        var receipts = await client.GetFromJsonAsync<JsonElement>("/incidents/INC-API-1/receipts", Ct);
        Assert.True(receipts.GetArrayLength() >= 9);
    }

    [Fact]
    public async Task Invalid_incident_is_rejected()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/incidents", new { title = "x" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Azure_monitor_alert_starts_an_investigation()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/webhooks/azure-monitor", new
        {
            schemaId = "azureMonitorCommonAlertSchema",
            data = new
            {
                essentials = new
                {
                    alertId = "/subscriptions/x/providers/Microsoft.AlertsManagement/alerts/abc123",
                    alertRule = "payments-api 5xx rate > 5%",
                    severity = "Sev1",
                    monitorCondition = "Fired",
                    configurationItems = new[] { "payments-api" },
                    description = "HTTP 502 rate above threshold",
                },
            },
        }, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var state = await WaitForPhaseAsync(client, "AM-abc123", "AwaitingApproval", "Escalated", "Failed");
        Assert.Equal("AwaitingApproval", state.GetProperty("phase").GetString());
    }

    [Fact]
    public async Task Tools_endpoint_shows_risk_classification()
    {
        var client = factory.CreateClient();

        var tools = await client.GetFromJsonAsync<JsonElement>("/tools", Ct);

        var rollback = tools.EnumerateArray().Single(t => t.GetProperty("name").GetString() == "rollback_deployment");
        Assert.Equal("ProductionChanging", rollback.GetProperty("risk").GetString());
    }
}
