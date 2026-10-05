using ProductionIncident.Core.Contracts;

namespace ProductionIncident.Agents;

/// <summary>
/// Explicitly allowed handoff routes (blueprint §9.2, security principle 10). Anything else is rejected.
/// </summary>
public static class HandoffTopology
{
    private static readonly Dictionary<string, string[]> Routes = new(StringComparer.OrdinalIgnoreCase)
    {
        [AgentNames.Triage] = [AgentNames.Logs, AgentNames.Database, AgentNames.Metrics, AgentNames.Deployment],
        [AgentNames.Logs] = [AgentNames.Database, AgentNames.Metrics, AgentNames.Deployment],
        [AgentNames.Database] = [AgentNames.Logs, AgentNames.Metrics],
        [AgentNames.Metrics] = [AgentNames.Logs, AgentNames.Database],
        [AgentNames.Deployment] = [AgentNames.Logs, AgentNames.Database],
    };

    public static IReadOnlyList<string> AllowedTargets(string from) =>
        Routes.TryGetValue(from, out var targets) ? targets : [];

    public static bool IsAllowed(string from, string to) =>
        AllowedTargets(from).Contains(to, StringComparer.OrdinalIgnoreCase);
}
