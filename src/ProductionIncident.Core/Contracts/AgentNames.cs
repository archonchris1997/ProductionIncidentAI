namespace ProductionIncident.Core.Contracts;

public static class AgentNames
{
    public const string Triage = "TriageAgent";
    public const string Supervisor = "SupervisorAgent";
    public const string Logs = "LogsAgent";
    public const string Database = "DatabaseAgent";
    public const string Metrics = "MetricsAgent";
    public const string Deployment = "DeploymentAgent";
    public const string RootCause = "RootCauseAgent";
    public const string Remediation = "RemediationAgent";

    public static readonly IReadOnlyList<string> Specialists = [Logs, Database, Metrics, Deployment];

    /// <summary>
    /// Maps the loose names an LLM may produce ("DBAgent", "db", "logs") to canonical specialist names.
    /// Returns null when the name is not a known specialist (deterministic code rejects it).
    /// </summary>
    public static string? NormalizeSpecialist(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var key = name.Trim().ToLowerInvariant().Replace("agent", "", StringComparison.Ordinal).Replace("_", "").Replace("-", "").Trim();
        return key switch
        {
            "logs" or "log" => Logs,
            "database" or "db" or "sql" => Database,
            "metrics" or "metric" => Metrics,
            "deployment" or "deploy" or "deployments" or "release" => Deployment,
            _ => null,
        };
    }
}
