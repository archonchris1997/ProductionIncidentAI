using ProductionIncident.Mcp.Models;

namespace ProductionIncident.Mcp.Backends.Fake;

/// <summary>
/// Three reference incidents used for local runs, workflow tests and the regression dataset:
///  - checkout-api : DB connection leak introduced by release v1.42 (needs a deep dive to resolve a contradiction)
///  - search-api   : CPU saturation caused by a traffic surge (no deployment involved)
///  - payments-api : bad configuration change (HTTP client timeout lowered to 500 ms)
/// </summary>
public static class FakeScenarios
{
    private static readonly DateTimeOffset Day = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<FakeScenario> All { get; } = [CheckoutConnectionLeak(), SearchCpuSaturation(), PaymentsBadConfig()];

    private static DateTimeOffset At(int hour, int minute) => Day.AddHours(hour).AddMinutes(minute);

    private static MetricSeriesResult Series(string service, string metric, string unit, double baseline, double peak, double current, DateTimeOffset start, DateTimeOffset end, string summary)
    {
        var points = new List<MetricPoint>
        {
            new(start.AddMinutes(-30), baseline),
            new(start.AddMinutes(-15), baseline),
            new(start, (baseline + peak) / 2),
            new(start.AddMinutes(10), peak),
            new(end, peak),
            new(end.AddMinutes(8), current),
        };
        return new MetricSeriesResult(service, metric, unit, baseline, current, peak, points, summary);
    }

    // ─────────────────────────────────────────────────────────────────────
    public static FakeScenario CheckoutConnectionLeak()
    {
        const string svc = "checkout-api";
        const string db = "orders-db";
        var t0 = At(14, 4);
        var end = At(14, 31);

        var errors = new RecentErrorsResult(svc,
        [
            new ErrorGroup("Microsoft.Data.SqlClient.SqlException",
                "Timeout expired. The timeout period elapsed prior to obtaining a connection from the pool. This may have occurred because all pooled connections were in use and max pool size was reached.",
                1843, t0, end, ["t-9f2a41", "t-77c1d0"]),
            new ErrorGroup("Microsoft.AspNetCore.Http.BadHttpRequestException",
                "HTTP 500 returned for POST /api/checkout", 2210, t0, end, ["t-9f2a41"]),
        ],
            "2 error groups for checkout-api since 14:04 UTC: 1843x SqlException 'Timeout expired ... prior to obtaining a connection from the pool ... max pool size was reached' (SQL timeout / connection pool exhaustion) and 2210x HTTP 500 on POST /api/checkout. Sample trace: t-9f2a41.");

        var logs = new List<LogEntry>
        {
            new(At(13, 50), svc, "Information", "Application started. Version v1.42", null, null),
            new(At(14, 2), svc, "Warning", "Slow SQL connection acquisition: 4.8s (OrderRepository.GetOpenOrdersAsync)", null, "t-5512aa"),
            new(t0, svc, "Error", "Timeout expired. The timeout period elapsed prior to obtaining a connection from the pool.", "Microsoft.Data.SqlClient.SqlException", "t-9f2a41"),
            new(At(14, 10), svc, "Error", "HTTP 500 POST /api/checkout", "Microsoft.AspNetCore.Http.BadHttpRequestException", "t-9f2a41"),
            new(At(14, 33), svc, "Warning", "Liveness probe failed; pod checkout-api-7d9c-x2 restarted", null, null),
        };

        var traces = new List<TraceResult>
        {
            new("t-9f2a41", 30012,
            [
                new TraceSpan("POST /api/checkout", svc, 30012, "Error", "HTTP 500"),
                new TraceSpan("OrderRepository.GetOpenOrdersAsync", svc, 30004, "Error", "Waited 30000 ms for a pooled SqlConnection to orders-db"),
                new TraceSpan("SqlConnection.Open", db, 30000, "Error", "Pool timeout (max pool size 100 reached)"),
            ],
                "Trace t-9f2a41: POST /api/checkout took 30012 ms; 30000 ms spent in OrderRepository.GetOpenOrdersAsync waiting for a pooled SqlConnection to orders-db (pool timeout, max pool size 100 reached). No slow query executed."),
        };

        var health = new DatabaseHealthResult(db, "Healthy", 22, 34, 100, At(14, 40),
            "orders-db is currently healthy (measured 14:40 UTC): CPU 22%, 34/100 active connections, no blocking sessions. This is a current snapshot only.");

        var pool = new ConnectionPoolResult(db, svc,
        [
            new PoolSample(At(13, 45), 12, 4, 100, 0),
            new PoolSample(At(13, 55), 38, 30, 100, 0),
            new PoolSample(At(14, 0), 71, 62, 100, 0),
            new PoolSample(t0, 100, 88, 100, 57),
            new PoolSample(At(14, 20), 100, 88, 100, 61),
            new PoolSample(end, 100, 88, 100, 49),
            new PoolSample(At(14, 33), 8, 2, 100, 0),
            new PoolSample(At(14, 40), 34, 26, 100, 0),
        ],
            new Dictionary<string, int> { [svc] = 88, ["reporting-worker"] = 3 },
            "Connection pool for orders-db was saturated 100/100 from 14:04 to 14:31 UTC with up to 61 waiting requests. 88 idle (sleeping > 10 min) sessions held by checkout-api: connections opened but never returned to the pool (connection leak pattern). Usage grew linearly since 13:50, dropped after pod restarts at 14:33 and is growing again (34 at 14:40).");

        var metrics = new Dictionary<string, MetricSeriesResult>
        {
            ["error_rate"] = Series(svc, "error_rate", "%", 0.2, 38, 9, t0, end, "checkout-api error rate rose from 0.2% baseline to 38% peak at 14:10 UTC (currently 9%)."),
            ["latency"] = Series(svc, "latency_p99", "ms", 180, 30050, 2400, t0, end, "checkout-api p99 latency rose from 180 ms to 30050 ms (≈ the 30 s SQL connection timeout) since 14:04 UTC."),
            ["cpu"] = Series(svc, "cpu", "%", 35, 41, 31, t0, end, "checkout-api CPU normal: 35% baseline, 41% peak — no CPU saturation."),
            ["memory"] = Series(svc, "memory", "%", 55, 58, 54, t0, end, "checkout-api memory normal: 55% baseline, 58% peak."),
            ["request_rate"] = Series(svc, "request_rate", "rpm", 420, 445, 430, t0, end, "checkout-api request rate normal: 420 rpm baseline, 445 rpm peak — not load-driven."),
        };

        var recovered = new Dictionary<string, MetricSeriesResult>
        {
            ["error_rate"] = Series(svc, "error_rate", "%", 0.2, 38, 0.3, t0, end, "checkout-api error rate back to 0.3% after mitigation."),
            ["latency"] = Series(svc, "latency_p99", "ms", 180, 30050, 190, t0, end, "checkout-api p99 latency back to 190 ms after mitigation."),
            ["cpu"] = metrics["cpu"],
            ["memory"] = metrics["memory"],
            ["request_rate"] = metrics["request_rate"],
        };

        var deployments = new RecentDeploymentsResult(svc,
        [
            new DeploymentRecord(svc, "v1.42", "v1.41", At(13, 50), "team-checkout", "Succeeded"),
            new DeploymentRecord(svc, "v1.41", "v1.40", Day.AddDays(-3).AddHours(10), "team-checkout", "Succeeded"),
        ],
            "Latest deployment of checkout-api: v1.42 at 13:50 UTC (14 minutes before the incident start at 14:04), previous version v1.41 (deployed 3 days ago, stable).");

        var diff = new ReleaseDiffResult(svc, "v1.41", "v1.42",
        [
            new FileChange("src/Checkout.Infrastructure/OrderRepository.cs", "modified",
                "- await using var connection = await _factory.OpenAsync(ct);\n+ var connection = await _factory.OpenAsync(ct);"),
            new FileChange("src/Checkout.Api/CheckoutController.cs", "modified", "+ _logger.LogInformation(\"Checkout started {CartId}\", cartId);"),
        ],
            "Release diff v1.41 → v1.42: OrderRepository.GetOpenOrdersAsync no longer disposes the SqlConnection ('await using' removed), so connections are not returned to the pool. Other change: extra logging in CheckoutController.");

        return new FakeScenario
        {
            Name = "checkout-db-connection-leak",
            Service = svc,
            Database = db,
            IncidentStart = t0,
            RecentErrors = errors,
            Logs = logs,
            Traces = traces,
            DatabaseHealth = health,
            ConnectionPool = pool,
            SlowQueries = new SlowQueriesResult(db, [], "No slow queries on orders-db in the window (all queries < 50 ms)."),
            Deadlocks = new DeadlocksResult(db, 0, "No deadlocks on orders-db in the window."),
            Metrics = metrics,
            RecoveredMetrics = recovered,
            Deployments = deployments,
            ReleaseDiffs = [diff],
            ConfigChanges = new ConfigChangesResult(svc, [], "No configuration changes for checkout-api in the window."),
            DeploymentStatus = new DeploymentStatusResult(svc, "v1.42", 3, "Degraded", "checkout-api runs v1.42 on 3 replicas; health Degraded."),
            EffectiveOperations = new HashSet<string>(["rollback_deployment"], StringComparer.OrdinalIgnoreCase),
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    public static FakeScenario SearchCpuSaturation()
    {
        const string svc = "search-api";
        const string db = "search-db";
        var t0 = At(14, 18);
        var end = At(14, 45);

        var errors = new RecentErrorsResult(svc,
        [
            new ErrorGroup("System.Threading.Tasks.TaskCanceledException", "Request timed out after 5 s in SearchController.Query", 920, t0, end, ["t-3c0b11"]),
            new ErrorGroup("ThreadPoolStarvation", "Thread pool starvation detected: queue length 1840", 41, t0, end, []),
        ],
            "2 error groups for search-api since 14:18 UTC: 920x request timeouts after 5 s in SearchController.Query and 41x thread pool starvation warnings. Sample trace: t-3c0b11.");

        var traces = new List<TraceResult>
        {
            new("t-3c0b11", 4980,
            [
                new TraceSpan("GET /api/search", svc, 4980, "Error", "Timeout"),
                new TraceSpan("RankingService.Score", svc, 4610, "Ok", "CPU-bound ranking; no I/O wait"),
                new TraceSpan("SearchRepository.Query", db, 38, "Ok", null),
            ],
                "Trace t-3c0b11: GET /api/search took 4980 ms; 4610 ms CPU-bound in RankingService.Score, database query only 38 ms."),
        };

        var metrics = new Dictionary<string, MetricSeriesResult>
        {
            ["error_rate"] = Series(svc, "error_rate", "%", 0.3, 12, 11, t0, end, "search-api error rate rose from 0.3% to 12% since 14:18 UTC."),
            ["latency"] = Series(svc, "latency_p99", "ms", 220, 4980, 4900, t0, end, "search-api p99 latency rose from 220 ms to 4980 ms since 14:18 UTC."),
            ["cpu"] = Series(svc, "cpu", "%", 40, 98, 97, t0, end, "search-api CPU saturated: 97–98% on all 4 replicas since 14:18 UTC (baseline 40%)."),
            ["memory"] = Series(svc, "memory", "%", 60, 63, 62, t0, end, "search-api memory normal: 60% baseline, 63% peak."),
            ["request_rate"] = Series(svc, "request_rate", "rpm", 1200, 3720, 3700, t0, end, "search-api request rate surge: 3700 rpm vs 1200 rpm baseline (3.1x) since 14:15 UTC — traffic-driven."),
        };

        var recovered = new Dictionary<string, MetricSeriesResult>(metrics)
        {
            ["error_rate"] = Series(svc, "error_rate", "%", 0.3, 12, 0.4, t0, end, "search-api error rate back to 0.4% after mitigation."),
            ["latency"] = Series(svc, "latency_p99", "ms", 220, 4980, 260, t0, end, "search-api p99 latency back to 260 ms after mitigation."),
            ["cpu"] = Series(svc, "cpu", "%", 40, 98, 45, t0, end, "search-api CPU back to 45% after mitigation."),
        };

        return new FakeScenario
        {
            Name = "search-cpu-saturation",
            Service = svc,
            Database = db,
            IncidentStart = t0,
            RecentErrors = errors,
            Logs =
            [
                new LogEntry(t0, svc, "Error", "Request timed out after 5 s in SearchController.Query", "System.Threading.Tasks.TaskCanceledException", "t-3c0b11"),
                new LogEntry(At(14, 20), svc, "Warning", "Thread pool starvation detected: queue length 1840", "ThreadPoolStarvation", null),
            ],
            Traces = traces,
            DatabaseHealth = new DatabaseHealthResult(db, "Healthy", 18, 25, 200, At(14, 50), "search-db is currently healthy: CPU 18%, 25/200 active connections."),
            ConnectionPool = new ConnectionPoolResult(db, svc,
                [new PoolSample(At(14, 0), 22, 18, 200, 0), new PoolSample(At(14, 30), 27, 20, 200, 0)],
                new Dictionary<string, int> { [svc] = 20 },
                "Connection pool for search-db normal during the window: 22–27/200 active, no waiting requests."),
            SlowQueries = new SlowQueriesResult(db, [], "No slow queries on search-db in the window."),
            Deadlocks = new DeadlocksResult(db, 0, "No deadlocks on search-db in the window."),
            Metrics = metrics,
            RecoveredMetrics = recovered,
            Deployments = new RecentDeploymentsResult(svc,
                [new DeploymentRecord(svc, "v3.8.0", "v3.7.2", Day.AddDays(-3).AddHours(9), "team-search", "Succeeded")],
                "No recent deployment of search-api: latest is v3.8.0, deployed 3 days ago."),
            ReleaseDiffs = [],
            ConfigChanges = new ConfigChangesResult(svc, [], "No configuration changes for search-api in the window."),
            DeploymentStatus = new DeploymentStatusResult(svc, "v3.8.0", 4, "Degraded", "search-api runs v3.8.0 on 4 replicas; health Degraded."),
            EffectiveOperations = new HashSet<string>(["scale_service"], StringComparer.OrdinalIgnoreCase),
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    public static FakeScenario PaymentsBadConfig()
    {
        const string svc = "payments-api";
        const string db = "payments-db";
        var t0 = At(9, 12);
        var end = At(9, 40);

        var errors = new RecentErrorsResult(svc,
        [
            new ErrorGroup("System.Threading.Tasks.TaskCanceledException",
                "The request was canceled due to the configured HttpClient.Timeout of 0.5 seconds elapsing (PaymentGatewayClient.AuthorizeAsync)",
                640, t0, end, ["t-a81e02"]),
        ],
            "1 error group for payments-api since 09:12 UTC: 640x HttpClient timeout of 0.5 seconds when calling payment-gateway (PaymentGatewayClient.AuthorizeAsync). Sample trace: t-a81e02.");

        var traces = new List<TraceResult>
        {
            new("t-a81e02", 512,
            [
                new TraceSpan("POST /api/payments/authorize", svc, 512, "Error", "HTTP 502"),
                new TraceSpan("PaymentGatewayClient.AuthorizeAsync", "payment-gateway", 500, "Error", "Canceled by client timeout at 500 ms; gateway p50 latency is 800 ms"),
            ],
                "Trace t-a81e02: authorize call canceled by the client timeout at 500 ms, while payment-gateway normally answers in ~800 ms (p50)."),
        };

        var metrics = new Dictionary<string, MetricSeriesResult>
        {
            ["error_rate"] = Series(svc, "error_rate", "%", 0.1, 22, 21, t0, end, "payments-api error rate rose from 0.1% to 22% since 09:12 UTC."),
            ["latency"] = Series(svc, "latency_p99", "ms", 1400, 1400, 510, t0, end, "payments-api p99 latency dropped to 510 ms (capped by a client timeout) since 09:12 UTC."),
            ["cpu"] = Series(svc, "cpu", "%", 30, 32, 29, t0, end, "payments-api CPU normal: 30% baseline."),
            ["memory"] = Series(svc, "memory", "%", 48, 49, 48, t0, end, "payments-api memory normal: 48%."),
            ["request_rate"] = Series(svc, "request_rate", "rpm", 300, 310, 305, t0, end, "payments-api request rate normal: ~300 rpm."),
        };

        var recovered = new Dictionary<string, MetricSeriesResult>(metrics)
        {
            ["error_rate"] = Series(svc, "error_rate", "%", 0.1, 22, 0.1, t0, end, "payments-api error rate back to 0.1% after mitigation."),
        };

        return new FakeScenario
        {
            Name = "payments-bad-config",
            Service = svc,
            Database = db,
            IncidentStart = t0,
            RecentErrors = errors,
            Logs =
            [
                new LogEntry(At(9, 10), svc, "Information", "Configuration reloaded: PaymentGateway:TimeoutMs=500", null, null),
                new LogEntry(t0, svc, "Error", "The request was canceled due to the configured HttpClient.Timeout of 0.5 seconds elapsing", "System.Threading.Tasks.TaskCanceledException", "t-a81e02"),
            ],
            Traces = traces,
            DatabaseHealth = new DatabaseHealthResult(db, "Healthy", 12, 15, 100, At(9, 45), "payments-db is currently healthy: CPU 12%, 15/100 active connections."),
            ConnectionPool = new ConnectionPoolResult(db, svc,
                [new PoolSample(At(9, 0), 14, 10, 100, 0), new PoolSample(At(9, 30), 15, 11, 100, 0)],
                new Dictionary<string, int> { [svc] = 11 },
                "Connection pool for payments-db normal during the window: 14–15/100 active, no waiting requests."),
            SlowQueries = new SlowQueriesResult(db, [], "No slow queries on payments-db in the window."),
            Deadlocks = new DeadlocksResult(db, 0, "No deadlocks on payments-db in the window."),
            Metrics = metrics,
            RecoveredMetrics = recovered,
            Deployments = new RecentDeploymentsResult(svc,
                [new DeploymentRecord(svc, "v2.3.1", "v2.3.0", Day.AddDays(-6).AddHours(11), "team-payments", "Succeeded")],
                "No recent deployment of payments-api: latest is v2.3.1, deployed 6 days ago."),
            ReleaseDiffs = [],
            ConfigChanges = new ConfigChangesResult(svc,
                [new ConfigChange("PaymentGateway:TimeoutMs", "5000", "500", At(9, 10), "config-sync-bot")],
                "Configuration change for payments-api at 09:10 UTC (2 minutes before the incident): PaymentGateway:TimeoutMs changed from 5000 to 500 by config-sync-bot. Previous value: 5000."),
            DeploymentStatus = new DeploymentStatusResult(svc, "v2.3.1", 3, "Degraded", "payments-api runs v2.3.1 on 3 replicas; health Degraded."),
            EffectiveOperations = new HashSet<string>(["change_configuration"], StringComparer.OrdinalIgnoreCase),
        };
    }
}
