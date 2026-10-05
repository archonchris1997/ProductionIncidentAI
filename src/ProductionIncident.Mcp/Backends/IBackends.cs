using ProductionIncident.Mcp.Models;

namespace ProductionIncident.Mcp.Backends;

// Backends are what real integrations replace (Milestone 6):
//   Logs       → Azure Log Analytics / Application Insights
//   Database   → SQL Server DMVs / PostgreSQL pg_stat_* / Cosmos diagnostics
//   Metrics    → Azure Monitor / Prometheus
//   Deployment → GitHub / Azure DevOps / Kubernetes
//   Operations → Kubernetes / deployment pipelines (write side, separate identity)

public interface ILogsBackend
{
    Task<RecentErrorsResult> GetRecentErrorsAsync(string service, int minutes, CancellationToken cancellationToken);

    Task<LogSearchResult> SearchLogsAsync(string query, string? service, int minutes, int limit, CancellationToken cancellationToken);

    Task<TraceResult> GetTraceAsync(string traceId, CancellationToken cancellationToken);
}

public interface IDatabaseBackend
{
    Task<DatabaseHealthResult> GetHealthAsync(string service, CancellationToken cancellationToken);

    Task<ConnectionPoolResult> GetConnectionPoolAsync(string service, int minutes, CancellationToken cancellationToken);

    Task<SlowQueriesResult> GetSlowQueriesAsync(string service, int minutes, CancellationToken cancellationToken);

    Task<DeadlocksResult> GetDeadlocksAsync(string service, int minutes, CancellationToken cancellationToken);
}

public interface IMetricsBackend
{
    Task<MetricSeriesResult> GetMetricAsync(string service, string metric, int minutes, CancellationToken cancellationToken);
}

public interface IDeploymentBackend
{
    Task<RecentDeploymentsResult> GetRecentDeploymentsAsync(string service, int hours, CancellationToken cancellationToken);

    Task<ReleaseDiffResult> GetReleaseDiffAsync(string service, string fromVersion, string toVersion, CancellationToken cancellationToken);

    Task<ConfigChangesResult> GetConfigChangesAsync(string service, int hours, CancellationToken cancellationToken);

    Task<DeploymentStatusResult> GetDeploymentStatusAsync(string service, CancellationToken cancellationToken);
}

public interface IOperationsBackend
{
    Task<OperationResult> RollbackDeploymentAsync(string service, string targetVersion, CancellationToken cancellationToken);

    Task<OperationResult> RestartServiceAsync(string service, CancellationToken cancellationToken);

    Task<OperationResult> ScaleServiceAsync(string service, int replicas, CancellationToken cancellationToken);

    Task<OperationResult> ChangeConfigurationAsync(string service, string key, string value, CancellationToken cancellationToken);

    Task<OperationResult> KillPodAsync(string service, string pod, CancellationToken cancellationToken);

    Task<OperationResult> ExecuteWriteSqlAsync(string database, string sql, CancellationToken cancellationToken);

    Task<OperationResult> DeleteResourceAsync(string resourceId, CancellationToken cancellationToken);
}
