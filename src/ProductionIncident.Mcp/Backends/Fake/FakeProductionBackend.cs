using System.Collections.Concurrent;
using ProductionIncident.Core.Text;
using ProductionIncident.Mcp.Models;

namespace ProductionIncident.Mcp.Backends.Fake;

/// <summary>
/// Deterministic fake of the production estate. Implements every backend so that the system runs
/// end-to-end with no cloud dependency. Operations mutate in-memory state (e.g. a rollback "recovers"
/// the service) so verification after an approved action can be exercised.
/// </summary>
public sealed class FakeProductionBackend : ILogsBackend, IDatabaseBackend, IMetricsBackend, IDeploymentBackend, IOperationsBackend
{
    private readonly Dictionary<string, FakeScenario> _byService;
    private readonly ConcurrentDictionary<string, bool> _recovered = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<OperationResult> _operations = new();

    public FakeProductionBackend()
        : this(FakeScenarios.All)
    {
    }

    public FakeProductionBackend(IEnumerable<FakeScenario> scenarios)
    {
        _byService = scenarios.ToDictionary(s => s.Service, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<OperationResult> ExecutedOperations => _operations.ToArray();

    public bool IsRecovered(string service) => _recovered.GetValueOrDefault(service);

    private FakeScenario? Find(string? service) =>
        service is not null && _byService.TryGetValue(service.Trim(), out var s) ? s : null;

    private FakeScenario? FindByDatabase(string database) =>
        _byService.Values.FirstOrDefault(s => string.Equals(s.Database, database, StringComparison.OrdinalIgnoreCase));

    // ── Logs ────────────────────────────────────────────────────────────
    public Task<RecentErrorsResult> GetRecentErrorsAsync(string service, int minutes, CancellationToken cancellationToken)
    {
        var s = Find(service);
        return Task.FromResult(s?.RecentErrors ?? new RecentErrorsResult(service, [], $"No errors found for '{service}' in the last {minutes} minutes."));
    }

    public Task<LogSearchResult> SearchLogsAsync(string query, string? service, int minutes, int limit, CancellationToken cancellationToken)
    {
        List<FakeScenario> scenarios = Find(service) is { } one ? [one] : [.. _byService.Values];
        var terms = TextSearch.Tokenize(query);
        var matches = scenarios
            .SelectMany(s => s.Logs)
            .Where(l => terms.Count == 0 || terms.Any(t => $"{l.Message} {l.ExceptionType}".Contains(t, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(l => l.Timestamp)
            .ToList();

        var page = matches.Take(Math.Clamp(limit, 1, 50)).ToList();
        var summary = matches.Count == 0
            ? $"No log lines match '{query}'."
            : $"{matches.Count} log line(s) match '{query}'. Most recent: [{page[0].Level}] {page[0].Message}";
        return Task.FromResult(new LogSearchResult(query, matches.Count, page, summary));
    }

    public Task<TraceResult> GetTraceAsync(string traceId, CancellationToken cancellationToken)
    {
        var trace = _byService.Values.SelectMany(s => s.Traces).FirstOrDefault(t => string.Equals(t.TraceId, traceId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(trace ?? new TraceResult(traceId, 0, [], $"Trace '{traceId}' not found."));
    }

    // ── Database ────────────────────────────────────────────────────────
    public Task<DatabaseHealthResult> GetHealthAsync(string service, CancellationToken cancellationToken)
    {
        var s = Find(service) ?? FindByDatabase(service);
        return Task.FromResult(s?.DatabaseHealth ?? new DatabaseHealthResult(service, "Unknown", 0, 0, 0, DateTimeOffset.UtcNow, $"No database registered for '{service}'."));
    }

    public Task<ConnectionPoolResult> GetConnectionPoolAsync(string service, int minutes, CancellationToken cancellationToken)
    {
        var s = Find(service) ?? FindByDatabase(service);
        return Task.FromResult(s?.ConnectionPool ?? new ConnectionPoolResult(service, service, [], new Dictionary<string, int>(), $"No connection pool data for '{service}'."));
    }

    public Task<SlowQueriesResult> GetSlowQueriesAsync(string service, int minutes, CancellationToken cancellationToken)
    {
        var s = Find(service) ?? FindByDatabase(service);
        return Task.FromResult(s?.SlowQueries ?? new SlowQueriesResult(service, [], $"No slow query data for '{service}'."));
    }

    public Task<DeadlocksResult> GetDeadlocksAsync(string service, int minutes, CancellationToken cancellationToken)
    {
        var s = Find(service) ?? FindByDatabase(service);
        return Task.FromResult(s?.Deadlocks ?? new DeadlocksResult(service, 0, $"No deadlock data for '{service}'."));
    }

    // ── Metrics ─────────────────────────────────────────────────────────
    public Task<MetricSeriesResult> GetMetricAsync(string service, string metric, int minutes, CancellationToken cancellationToken)
    {
        var s = Find(service);
        if (s is null)
        {
            return Task.FromResult(new MetricSeriesResult(service, metric, "", 0, 0, 0, [], $"No '{metric}' data for '{service}'."));
        }

        var source = IsRecovered(service) ? s.RecoveredMetrics : s.Metrics;
        return Task.FromResult(source.TryGetValue(metric, out var series)
            ? series
            : new MetricSeriesResult(service, metric, "", 0, 0, 0, [], $"Metric '{metric}' not available for '{service}'."));
    }

    // ── Deployment ──────────────────────────────────────────────────────
    public Task<RecentDeploymentsResult> GetRecentDeploymentsAsync(string service, int hours, CancellationToken cancellationToken)
    {
        var s = Find(service);
        return Task.FromResult(s?.Deployments ?? new RecentDeploymentsResult(service, [], $"No deployments found for '{service}'."));
    }

    public Task<ReleaseDiffResult> GetReleaseDiffAsync(string service, string fromVersion, string toVersion, CancellationToken cancellationToken)
    {
        var diff = Find(service)?.ReleaseDiffs.FirstOrDefault(d =>
            string.Equals(d.FromVersion, fromVersion, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(d.ToVersion, toVersion, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(diff ?? new ReleaseDiffResult(service, fromVersion, toVersion, [], $"No diff available for {service} {fromVersion} → {toVersion}."));
    }

    public Task<ConfigChangesResult> GetConfigChangesAsync(string service, int hours, CancellationToken cancellationToken)
    {
        var s = Find(service);
        return Task.FromResult(s?.ConfigChanges ?? new ConfigChangesResult(service, [], $"No configuration changes for '{service}'."));
    }

    public Task<DeploymentStatusResult> GetDeploymentStatusAsync(string service, CancellationToken cancellationToken)
    {
        var s = Find(service);
        if (s is null)
        {
            return Task.FromResult(new DeploymentStatusResult(service, "unknown", 0, "Unknown", $"Service '{service}' not found."));
        }

        return Task.FromResult(IsRecovered(service)
            ? s.DeploymentStatus with { Health = "Healthy", Summary = $"{service} healthy after mitigation." }
            : s.DeploymentStatus);
    }

    // ── Operations ──────────────────────────────────────────────────────
    public Task<OperationResult> RollbackDeploymentAsync(string service, string targetVersion, CancellationToken cancellationToken) =>
        Apply("rollback_deployment", service, $"{service} rolled back to {targetVersion}.");

    public Task<OperationResult> RestartServiceAsync(string service, CancellationToken cancellationToken) =>
        Apply("restart_service", service, $"{service} restarted.");

    public Task<OperationResult> ScaleServiceAsync(string service, int replicas, CancellationToken cancellationToken) =>
        Apply("scale_service", service, $"{service} scaled to {replicas} replicas.");

    public Task<OperationResult> ChangeConfigurationAsync(string service, string key, string value, CancellationToken cancellationToken) =>
        Apply("change_configuration", service, $"{service}: {key} set to '{value}'.");

    public Task<OperationResult> KillPodAsync(string service, string pod, CancellationToken cancellationToken) =>
        Apply("kill_pod", service, $"Pod {pod} of {service} killed.");

    public Task<OperationResult> ExecuteWriteSqlAsync(string database, string sql, CancellationToken cancellationToken) =>
        Apply("execute_write_sql", FindByDatabase(database)?.Service ?? database, $"Write SQL executed on {database}.");

    public Task<OperationResult> DeleteResourceAsync(string resourceId, CancellationToken cancellationToken) =>
        Apply("delete_resource", resourceId, $"Resource {resourceId} deleted.");

    private Task<OperationResult> Apply(string operation, string service, string message)
    {
        var s = Find(service);
        if (s is not null && s.EffectiveOperations.Contains(operation))
        {
            _recovered[s.Service] = true;
        }

        var result = new OperationResult(operation, service, true, message);
        _operations.Enqueue(result);
        return Task.FromResult(result);
    }
}
