namespace ProductionIncident.Mcp.Models;

// Tool result DTOs. Every result carries a short `Summary` designed for model consumption:
// MCP servers should return condensed, minimized data rather than raw dumps (security principle 8).

// ── Logs ────────────────────────────────────────────────────────────────
public sealed record LogEntry(DateTimeOffset Timestamp, string Service, string Level, string Message, string? ExceptionType, string? TraceId);

public sealed record ErrorGroup(string ExceptionType, string Message, int Count, DateTimeOffset FirstSeen, DateTimeOffset LastSeen, IReadOnlyList<string> SampleTraceIds);

public sealed record RecentErrorsResult(string Service, IReadOnlyList<ErrorGroup> Groups, string Summary);

public sealed record LogSearchResult(string Query, int TotalMatches, IReadOnlyList<LogEntry> Entries, string Summary);

public sealed record TraceSpan(string Name, string Service, double DurationMs, string Status, string? Detail);

public sealed record TraceResult(string TraceId, double TotalDurationMs, IReadOnlyList<TraceSpan> Spans, string Summary);

// ── Database ────────────────────────────────────────────────────────────
public sealed record DatabaseHealthResult(string Database, string Status, double CpuPercent, int ActiveConnections, int MaxConnections, DateTimeOffset MeasuredAt, string Summary);

public sealed record PoolSample(DateTimeOffset At, int Active, int Idle, int Max, int Waiting);

public sealed record ConnectionPoolResult(string Database, string Service, IReadOnlyList<PoolSample> History, IReadOnlyDictionary<string, int> IdleSessionsByClient, string Summary);

public sealed record SlowQuery(string QueryHash, string Text, double AvgDurationMs, int Executions);

public sealed record SlowQueriesResult(string Database, IReadOnlyList<SlowQuery> Queries, string Summary);

public sealed record DeadlocksResult(string Database, int Count, string Summary);

// ── Metrics ─────────────────────────────────────────────────────────────
public sealed record MetricPoint(DateTimeOffset At, double Value);

public sealed record MetricSeriesResult(string Service, string Metric, string Unit, double Baseline, double Current, double Peak, IReadOnlyList<MetricPoint> Series, string Summary);

// ── Deployment ──────────────────────────────────────────────────────────
public sealed record DeploymentRecord(string Service, string Version, string PreviousVersion, DateTimeOffset DeployedAt, string Author, string Status);

public sealed record RecentDeploymentsResult(string Service, IReadOnlyList<DeploymentRecord> Deployments, string Summary);

public sealed record FileChange(string Path, string ChangeType, string Excerpt);

public sealed record ReleaseDiffResult(string Service, string FromVersion, string ToVersion, IReadOnlyList<FileChange> Changes, string Summary);

public sealed record ConfigChange(string Key, string OldValue, string NewValue, DateTimeOffset ChangedAt, string ChangedBy);

public sealed record ConfigChangesResult(string Service, IReadOnlyList<ConfigChange> Changes, string Summary);

public sealed record DeploymentStatusResult(string Service, string CurrentVersion, int Replicas, string Health, string Summary);

// ── Operations (production-changing) ───────────────────────────────────
public sealed record OperationResult(string Operation, string Target, bool Success, string Message);
