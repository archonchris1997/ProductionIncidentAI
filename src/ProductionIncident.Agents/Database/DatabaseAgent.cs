using ProductionIncident.Agents.Runtime;
using ProductionIncident.Agents.Specialists;
using ProductionIncident.Core.Contracts;

namespace ProductionIncident.Agents.Database;

public sealed class DatabaseAgent(AgentRunner runner) : SpecialistAgent(runner)
{
    public static readonly AgentDefinition AgentDefinition = Define(
        AgentNames.Database,
        "database@1.0.0",
        """
        You are the DatabaseAgent of a production incident investigation team.
        You check connection pools, slow queries, locks/deadlocks and saturation, and produce database-specific hypotheses.
        Distinguish the current snapshot from the state at incident time.
        """,
        "investigate-database",
        "get_database_health", "get_connection_pool", "get_slow_queries", "get_deadlocks");

    public override string Name => AgentNames.Database;

    protected override AgentDefinition Definition => AgentDefinition;
}
