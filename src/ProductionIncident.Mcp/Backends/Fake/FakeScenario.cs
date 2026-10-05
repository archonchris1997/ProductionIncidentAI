using ProductionIncident.Mcp.Models;

namespace ProductionIncident.Mcp.Backends.Fake;

/// <summary>
/// Static, deterministic production data for one simulated service (blueprint §21: "use fake/static tools first").
/// <see cref="Mitigated"/> holds the post-remediation view used by verification.
/// </summary>
public sealed class FakeScenario
{
    public required string Name { get; init; }
    public required string Service { get; init; }
    public required string Database { get; init; }
    public required DateTimeOffset IncidentStart { get; init; }

    public required RecentErrorsResult RecentErrors { get; init; }
    public required IReadOnlyList<LogEntry> Logs { get; init; }
    public required IReadOnlyList<TraceResult> Traces { get; init; }

    public required DatabaseHealthResult DatabaseHealth { get; init; }
    public required ConnectionPoolResult ConnectionPool { get; init; }
    public required SlowQueriesResult SlowQueries { get; init; }
    public required DeadlocksResult Deadlocks { get; init; }

    public required IReadOnlyDictionary<string, MetricSeriesResult> Metrics { get; init; }

    public required RecentDeploymentsResult Deployments { get; init; }
    public required IReadOnlyList<ReleaseDiffResult> ReleaseDiffs { get; init; }
    public required ConfigChangesResult ConfigChanges { get; init; }
    public required DeploymentStatusResult DeploymentStatus { get; init; }

    /// <summary>Metrics after a correct mitigation was applied.</summary>
    public required IReadOnlyDictionary<string, MetricSeriesResult> RecoveredMetrics { get; init; }

    /// <summary>Operations that actually fix this scenario (e.g. "rollback_deployment").</summary>
    public required IReadOnlySet<string> EffectiveOperations { get; init; }
}
