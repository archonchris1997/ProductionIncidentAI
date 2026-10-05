using ProductionIncident.Core.Tools;

namespace ProductionIncident.Mcp.Tools;

public static class ToolDomains
{
    public const string Logs = "logs";
    public const string Database = "database";
    public const string Metrics = "metrics";
    public const string Deployment = "deployment";
    public const string Operations = "operations";
    public const string Knowledge = "knowledge";
}

/// <summary>
/// Client-side, deterministic classification of every known tool. Remote MCP tool annotations are hints
/// from a server and are NOT trusted for safety decisions: a tool missing here is treated as production-changing.
/// </summary>
public static class ToolRegistry
{
    private static readonly Dictionary<string, (string Domain, ToolRisk Risk)> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        // Logs MCP
        ["search_logs"] = (ToolDomains.Logs, ToolRisk.ReadOnly),
        ["get_recent_errors"] = (ToolDomains.Logs, ToolRisk.ReadOnly),
        ["get_trace"] = (ToolDomains.Logs, ToolRisk.ReadOnly),

        // Database MCP
        ["get_database_health"] = (ToolDomains.Database, ToolRisk.ReadOnly),
        ["get_connection_pool"] = (ToolDomains.Database, ToolRisk.ReadOnly),
        ["get_slow_queries"] = (ToolDomains.Database, ToolRisk.ReadOnly),
        ["get_deadlocks"] = (ToolDomains.Database, ToolRisk.ReadOnly),

        // Metrics MCP
        ["get_cpu"] = (ToolDomains.Metrics, ToolRisk.ReadOnly),
        ["get_memory"] = (ToolDomains.Metrics, ToolRisk.ReadOnly),
        ["get_latency"] = (ToolDomains.Metrics, ToolRisk.ReadOnly),
        ["get_error_rate"] = (ToolDomains.Metrics, ToolRisk.ReadOnly),
        ["get_request_rate"] = (ToolDomains.Metrics, ToolRisk.ReadOnly),

        // Deployment MCP
        ["get_recent_deployments"] = (ToolDomains.Deployment, ToolRisk.ReadOnly),
        ["get_release_diff"] = (ToolDomains.Deployment, ToolRisk.ReadOnly),
        ["get_config_changes"] = (ToolDomains.Deployment, ToolRisk.ReadOnly),
        ["get_deployment_status"] = (ToolDomains.Deployment, ToolRisk.ReadOnly),

        // Operations MCP (write side, separate server + identity)
        ["rollback_deployment"] = (ToolDomains.Operations, ToolRisk.ProductionChanging),
        ["restart_service"] = (ToolDomains.Operations, ToolRisk.ProductionChanging),
        ["scale_service"] = (ToolDomains.Operations, ToolRisk.ProductionChanging),
        ["change_configuration"] = (ToolDomains.Operations, ToolRisk.ProductionChanging),
        ["kill_pod"] = (ToolDomains.Operations, ToolRisk.ProductionChanging),
        ["execute_write_sql"] = (ToolDomains.Operations, ToolRisk.ProductionChanging),
        ["delete_resource"] = (ToolDomains.Operations, ToolRisk.ProductionChanging),
    };

    public static (string Domain, ToolRisk Risk) Classify(string toolName) =>
        Known.TryGetValue(toolName, out var c) ? c : ("unknown", ToolRisk.ProductionChanging);

    public static bool IsKnown(string toolName) => Known.ContainsKey(toolName);

    public static IReadOnlyList<string> ToolsInDomain(string domain) =>
        Known.Where(k => k.Value.Domain == domain).Select(k => k.Key).ToList();
}
