using System.ComponentModel;
using ModelContextProtocol.Server;
using ProductionIncident.Mcp.Backends;
using ProductionIncident.Mcp.Models;

namespace ProductionIncident.Mcp.Tools;

// The same classes are exposed in-process (LocalToolCatalog, via AIFunctionFactory) and over MCP
// (ProductionIncident.McpServer, via WithTools<T>()). One definition, two transports.

[McpServerToolType]
public sealed class LogsTools(ILogsBackend backend)
{
    [McpServerTool(Name = "get_recent_errors", ReadOnly = true, Idempotent = true)]
    [Description("Returns recent error groups (exception type, message, count, first/last seen, sample trace ids) for a service.")]
    public Task<RecentErrorsResult> GetRecentErrors(
        [Description("Service name, e.g. checkout-api")] string service,
        [Description("Look-back window in minutes (5-1440)")] int minutes = 120,
        CancellationToken cancellationToken = default) =>
        backend.GetRecentErrorsAsync(ToolInput.Service(service), ToolInput.Window(minutes, 5, 1440), cancellationToken);

    [McpServerTool(Name = "search_logs", ReadOnly = true, Idempotent = true)]
    [Description("Searches log lines by free-text query, optionally scoped to a service.")]
    public Task<LogSearchResult> SearchLogs(
        [Description("Free-text query, e.g. 'connection pool'")] string query,
        [Description("Optional service name")] string? service = null,
        [Description("Look-back window in minutes (5-1440)")] int minutes = 120,
        [Description("Max lines to return (1-50)")] int limit = 20,
        CancellationToken cancellationToken = default) =>
        backend.SearchLogsAsync(
            ToolInput.Text(query, nameof(query), 200),
            string.IsNullOrWhiteSpace(service) ? null : ToolInput.Service(service),
            ToolInput.Window(minutes, 5, 1440),
            ToolInput.Window(limit, 1, 50),
            cancellationToken);

    [McpServerTool(Name = "get_trace", ReadOnly = true, Idempotent = true)]
    [Description("Returns the spans of a distributed trace with durations and status.")]
    public Task<TraceResult> GetTrace(
        [Description("Trace id, e.g. t-9f2a41")] string traceId,
        CancellationToken cancellationToken = default) =>
        backend.GetTraceAsync(ToolInput.Id(traceId, nameof(traceId)), cancellationToken);
}

[McpServerToolType]
public sealed class DatabaseTools(IDatabaseBackend backend)
{
    [McpServerTool(Name = "get_database_health", ReadOnly = true, Idempotent = true)]
    [Description("Current health snapshot of the database used by a service (status, CPU, active/max connections). Current state only, not history.")]
    public Task<DatabaseHealthResult> GetDatabaseHealth(
        [Description("Service name (or database name)")] string service,
        CancellationToken cancellationToken = default) =>
        backend.GetHealthAsync(ToolInput.Service(service), cancellationToken);

    [McpServerTool(Name = "get_connection_pool", ReadOnly = true, Idempotent = true)]
    [Description("Connection pool history over a window: active/idle/max/waiting samples and idle sessions per client application.")]
    public Task<ConnectionPoolResult> GetConnectionPool(
        [Description("Service name (or database name)")] string service,
        [Description("Look-back window in minutes (5-1440)")] int minutes = 120,
        CancellationToken cancellationToken = default) =>
        backend.GetConnectionPoolAsync(ToolInput.Service(service), ToolInput.Window(minutes, 5, 1440), cancellationToken);

    [McpServerTool(Name = "get_slow_queries", ReadOnly = true, Idempotent = true)]
    [Description("Slow queries over a window (normalized text, average duration, executions).")]
    public Task<SlowQueriesResult> GetSlowQueries(
        [Description("Service name (or database name)")] string service,
        [Description("Look-back window in minutes (5-1440)")] int minutes = 120,
        CancellationToken cancellationToken = default) =>
        backend.GetSlowQueriesAsync(ToolInput.Service(service), ToolInput.Window(minutes, 5, 1440), cancellationToken);

    [McpServerTool(Name = "get_deadlocks", ReadOnly = true, Idempotent = true)]
    [Description("Deadlock count over a window.")]
    public Task<DeadlocksResult> GetDeadlocks(
        [Description("Service name (or database name)")] string service,
        [Description("Look-back window in minutes (5-1440)")] int minutes = 120,
        CancellationToken cancellationToken = default) =>
        backend.GetDeadlocksAsync(ToolInput.Service(service), ToolInput.Window(minutes, 5, 1440), cancellationToken);
}

[McpServerToolType]
public sealed class MetricsTools(IMetricsBackend backend)
{
    [McpServerTool(Name = "get_cpu", ReadOnly = true, Idempotent = true)]
    [Description("CPU utilization (%) of a service: baseline, current, peak and series.")]
    public Task<MetricSeriesResult> GetCpu([Description("Service name")] string service, [Description("Window in minutes")] int minutes = 120, CancellationToken cancellationToken = default) =>
        Get(service, "cpu", minutes, cancellationToken);

    [McpServerTool(Name = "get_memory", ReadOnly = true, Idempotent = true)]
    [Description("Memory utilization (%) of a service: baseline, current, peak and series.")]
    public Task<MetricSeriesResult> GetMemory([Description("Service name")] string service, [Description("Window in minutes")] int minutes = 120, CancellationToken cancellationToken = default) =>
        Get(service, "memory", minutes, cancellationToken);

    [McpServerTool(Name = "get_latency", ReadOnly = true, Idempotent = true)]
    [Description("p99 latency (ms) of a service: baseline, current, peak and series.")]
    public Task<MetricSeriesResult> GetLatency([Description("Service name")] string service, [Description("Window in minutes")] int minutes = 120, CancellationToken cancellationToken = default) =>
        Get(service, "latency", minutes, cancellationToken);

    [McpServerTool(Name = "get_error_rate", ReadOnly = true, Idempotent = true)]
    [Description("Error rate (% of requests) of a service: baseline, current, peak and series.")]
    public Task<MetricSeriesResult> GetErrorRate([Description("Service name")] string service, [Description("Window in minutes")] int minutes = 120, CancellationToken cancellationToken = default) =>
        Get(service, "error_rate", minutes, cancellationToken);

    [McpServerTool(Name = "get_request_rate", ReadOnly = true, Idempotent = true)]
    [Description("Request rate (requests/min) of a service: baseline, current, peak and series.")]
    public Task<MetricSeriesResult> GetRequestRate([Description("Service name")] string service, [Description("Window in minutes")] int minutes = 120, CancellationToken cancellationToken = default) =>
        Get(service, "request_rate", minutes, cancellationToken);

    private Task<MetricSeriesResult> Get(string service, string metric, int minutes, CancellationToken cancellationToken) =>
        backend.GetMetricAsync(ToolInput.Service(service), metric, ToolInput.Window(minutes, 5, 1440), cancellationToken);
}

[McpServerToolType]
public sealed class DeploymentTools(IDeploymentBackend backend)
{
    [McpServerTool(Name = "get_recent_deployments", ReadOnly = true, Idempotent = true)]
    [Description("Recent deployments of a service (version, previous version, time, author, status).")]
    public Task<RecentDeploymentsResult> GetRecentDeployments(
        [Description("Service name")] string service,
        [Description("Look-back window in hours (1-336)")] int hours = 72,
        CancellationToken cancellationToken = default) =>
        backend.GetRecentDeploymentsAsync(ToolInput.Service(service), ToolInput.Window(hours, 1, 336), cancellationToken);

    [McpServerTool(Name = "get_release_diff", ReadOnly = true, Idempotent = true)]
    [Description("Code changes between two released versions of a service.")]
    public Task<ReleaseDiffResult> GetReleaseDiff(
        [Description("Service name")] string service,
        [Description("Previous version, e.g. v1.41")] string fromVersion,
        [Description("New version, e.g. v1.42")] string toVersion,
        CancellationToken cancellationToken = default) =>
        backend.GetReleaseDiffAsync(ToolInput.Service(service), ToolInput.Version(fromVersion, nameof(fromVersion)), ToolInput.Version(toVersion, nameof(toVersion)), cancellationToken);

    [McpServerTool(Name = "get_config_changes", ReadOnly = true, Idempotent = true)]
    [Description("Configuration changes of a service over a window (key, old/new value, time, author).")]
    public Task<ConfigChangesResult> GetConfigChanges(
        [Description("Service name")] string service,
        [Description("Look-back window in hours (1-336)")] int hours = 24,
        CancellationToken cancellationToken = default) =>
        backend.GetConfigChangesAsync(ToolInput.Service(service), ToolInput.Window(hours, 1, 336), cancellationToken);

    [McpServerTool(Name = "get_deployment_status", ReadOnly = true, Idempotent = true)]
    [Description("Current deployment status of a service (version, replicas, health).")]
    public Task<DeploymentStatusResult> GetDeploymentStatus(
        [Description("Service name")] string service,
        CancellationToken cancellationToken = default) =>
        backend.GetDeploymentStatusAsync(ToolInput.Service(service), cancellationToken);
}
