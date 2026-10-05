using ProductionIncident.Agents.Runtime;
using ProductionIncident.Agents.Specialists;
using ProductionIncident.Core.Contracts;

namespace ProductionIncident.Agents.Logs;

public sealed class LogsAgent(AgentRunner runner) : SpecialistAgent(runner)
{
    public static readonly AgentDefinition AgentDefinition = Define(
        AgentNames.Logs,
        "logs@1.0.0",
        """
        You are the LogsAgent of a production incident investigation team.
        You inspect exceptions, group recurring errors, correlate trace ids and build log evidence.
        """,
        "investigate-logs",
        "get_recent_errors", "search_logs", "get_trace");

    public override string Name => AgentNames.Logs;

    protected override AgentDefinition Definition => AgentDefinition;
}
