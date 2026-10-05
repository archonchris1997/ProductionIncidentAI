using System.ComponentModel;
using ModelContextProtocol.Server;
using ProductionIncident.Mcp.Backends;
using ProductionIncident.Mcp.Models;

namespace ProductionIncident.Mcp.Tools;

/// <summary>
/// Production-changing tools. Exposed by a separate MCP server profile ("operations") with its own identity,
/// and only ever invoked by the deterministic ActionExecutor after human approval — never by an agent loop.
/// </summary>
[McpServerToolType]
public sealed class OperationsTools(IOperationsBackend backend)
{
    [McpServerTool(Name = "rollback_deployment", Destructive = true, Idempotent = true)]
    [Description("Rolls a service back to a previous version.")]
    public Task<OperationResult> RollbackDeployment(
        [Description("Service name")] string service,
        [Description("Version to roll back to, e.g. v1.41")] string targetVersion,
        CancellationToken cancellationToken = default) =>
        backend.RollbackDeploymentAsync(ToolInput.Service(service), ToolInput.Version(targetVersion, nameof(targetVersion)), cancellationToken);

    [McpServerTool(Name = "restart_service", Destructive = true)]
    [Description("Performs a rolling restart of a service.")]
    public Task<OperationResult> RestartService(
        [Description("Service name")] string service,
        CancellationToken cancellationToken = default) =>
        backend.RestartServiceAsync(ToolInput.Service(service), cancellationToken);

    [McpServerTool(Name = "scale_service", Destructive = true, Idempotent = true)]
    [Description("Scales a service to a number of replicas (1-50).")]
    public Task<OperationResult> ScaleService(
        [Description("Service name")] string service,
        [Description("Target replica count (1-50)")] int replicas,
        CancellationToken cancellationToken = default)
    {
        if (replicas is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(replicas), "Replicas must be between 1 and 50.");
        }

        return backend.ScaleServiceAsync(ToolInput.Service(service), replicas, cancellationToken);
    }

    [McpServerTool(Name = "change_configuration", Destructive = true, Idempotent = true)]
    [Description("Sets a configuration key of a service.")]
    public Task<OperationResult> ChangeConfiguration(
        [Description("Service name")] string service,
        [Description("Configuration key, e.g. PaymentGateway:TimeoutMs")] string key,
        [Description("New value")] string value,
        CancellationToken cancellationToken = default) =>
        backend.ChangeConfigurationAsync(ToolInput.Service(service), ToolInput.ConfigKey(key), ToolInput.Text(value, nameof(value), 256), cancellationToken);

    [McpServerTool(Name = "kill_pod", Destructive = true)]
    [Description("Deletes a single pod of a service so it is recreated.")]
    public Task<OperationResult> KillPod(
        [Description("Service name")] string service,
        [Description("Pod name")] string pod,
        CancellationToken cancellationToken = default) =>
        backend.KillPodAsync(ToolInput.Service(service), ToolInput.Id(pod, nameof(pod)), cancellationToken);

    [McpServerTool(Name = "execute_write_sql", Destructive = true)]
    [Description("Executes a write SQL statement. HIGH RISK.")]
    public Task<OperationResult> ExecuteWriteSql(
        [Description("Database name")] string database,
        [Description("SQL statement")] string sql,
        CancellationToken cancellationToken = default) =>
        backend.ExecuteWriteSqlAsync(ToolInput.Service(database, nameof(database)), ToolInput.Text(sql, nameof(sql), 4000), cancellationToken);

    [McpServerTool(Name = "delete_resource", Destructive = true)]
    [Description("Deletes a cloud resource. HIGH RISK.")]
    public Task<OperationResult> DeleteResource(
        [Description("Resource id")] string resourceId,
        CancellationToken cancellationToken = default) =>
        backend.DeleteResourceAsync(ToolInput.Id(resourceId, nameof(resourceId)), cancellationToken);
}
