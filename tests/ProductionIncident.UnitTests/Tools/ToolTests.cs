using ProductionIncident.Application.Workflows;
using ProductionIncident.Core.Contracts;
using ProductionIncident.Core.Tools;
using ProductionIncident.Mcp.Backends.Fake;
using ProductionIncident.Mcp.Catalog;
using ProductionIncident.Mcp.Tools;

namespace ProductionIncident.UnitTests.Tools;

public sealed class ToolTests
{
    private static LocalToolCatalog Catalog(FakeProductionBackend backend) =>
        new(new LogsTools(backend), new DatabaseTools(backend), new MetricsTools(backend), new DeploymentTools(backend), new OperationsTools(backend));

    [Fact]
    public async Task Catalog_classifies_every_tool_and_exposes_the_blueprint_tool_set()
    {
        var tools = await Catalog(new FakeProductionBackend()).GetToolsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(23, tools.Count);
        Assert.All(tools.Where(t => t.Domain == ToolDomains.Operations), t => Assert.Equal(ToolRisk.ProductionChanging, t.Risk));
        Assert.All(tools.Where(t => t.Domain != ToolDomains.Operations), t => Assert.Equal(ToolRisk.ReadOnly, t.Risk));
        Assert.Contains(tools, t => t.Name == "get_connection_pool");
    }

    [Fact]
    public void Unknown_tools_are_treated_as_production_changing() =>
        Assert.Equal(ToolRisk.ProductionChanging, ToolRegistry.Classify("drop_everything").Risk);

    [Theory]
    [InlineData("Checkout-API; DROP TABLE")]
    [InlineData("../../etc/passwd")]
    [InlineData("")]
    public void Tool_inputs_are_validated_server_side(string service) =>
        Assert.Throws<ArgumentException>(() => ToolInput.Service(service));

    [Fact]
    public async Task Fake_rollback_recovers_the_scenario()
    {
        var backend = new FakeProductionBackend();
        var metrics = new MetricsTools(backend);
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal(9, (await metrics.GetErrorRate("checkout-api", 15, ct)).Current);
        await new OperationsTools(backend).RollbackDeployment("checkout-api", "v1.41", ct);
        Assert.Equal(0.3, (await metrics.GetErrorRate("checkout-api", 15, ct)).Current);
    }

    [Fact]
    public async Task Local_ai_function_returns_json_with_summary()
    {
        var tool = (await Catalog(new FakeProductionBackend()).GetToolsAsync(TestContext.Current.CancellationToken)).Single(t => t.Name == "get_recent_errors");

        var raw = await tool.Function.InvokeAsync(new Microsoft.Extensions.AI.AIFunctionArguments(new Dictionary<string, object?> { ["service"] = "checkout-api" }), TestContext.Current.CancellationToken);
        var text = ToolResultFormatter.ToText(raw);

        Assert.Contains("prior to obtaining a connection from the pool", text);
        Assert.Contains("\"summary\"", text);
    }

    [Fact]
    public void Remediation_validator_enforces_tool_target_and_risk()
    {
        var plan = new RemediationPlan(
        [
            new ProposedAction("rollback_deployment", new Dictionary<string, object?> { ["service"] = "checkout-api", ["targetVersion"] = "v1.41" }, "low", "ok"),
            new ProposedAction("restart_service", new Dictionary<string, object?> { ["service"] = "billing-api" }, "low", "wrong service"),
            new ProposedAction("format_disk", new Dictionary<string, object?>(), "low", "unknown tool"),
            new ProposedAction("get_error_rate", new Dictionary<string, object?> { ["service"] = "checkout-api" }, "low", "read-only"),
            new ProposedAction("execute_write_sql", new Dictionary<string, object?> { ["database"] = "orders-db", ["sql"] = "KILL 52" }, "low", "risk understated"),
        ], [], [], []);

        var result = RemediationValidator.Validate(plan, ["checkout-api"]);

        Assert.Equal(new[] { "rollback_deployment", "execute_write_sql" }, result.Actions.Select(a => a.Tool));
        Assert.Equal("high", result.Actions.Single(a => a.Tool == "execute_write_sql").Risk);
        Assert.Equal(3, result.Rejected.Count);
    }
}
