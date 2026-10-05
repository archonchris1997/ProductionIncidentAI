using ProductionIncident.Agents.Runtime;
using ProductionIncident.Agents.Specialists;
using ProductionIncident.Core.Contracts;

namespace ProductionIncident.Agents.Metrics;

public sealed class MetricsAgent(AgentRunner runner) : SpecialistAgent(runner)
{
    public static readonly AgentDefinition AgentDefinition = Define(
        AgentNames.Metrics,
        "metrics@1.0.0",
        """
        You are the MetricsAgent of a production incident investigation team.
        You analyse CPU, memory, latency, throughput and error rate, comparing baseline with incident values.
        """,
        "analyze-metrics",
        "get_error_rate", "get_latency", "get_cpu", "get_memory", "get_request_rate");

    public override string Name => AgentNames.Metrics;

    protected override AgentDefinition Definition => AgentDefinition;
}
