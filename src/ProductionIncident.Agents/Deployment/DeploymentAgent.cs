using ProductionIncident.Agents.Runtime;
using ProductionIncident.Agents.Specialists;
using ProductionIncident.Core.Contracts;

namespace ProductionIncident.Agents.Deployment;

public sealed class DeploymentAgent(AgentRunner runner) : SpecialistAgent(runner)
{
    public static readonly AgentDefinition AgentDefinition = Define(
        AgentNames.Deployment,
        "deployment@1.0.0",
        """
        You are the DeploymentAgent of a production incident investigation team.
        You build the release/configuration timeline, inspect release diffs and correlate changes with the incident start.
        """,
        "deployment-analysis",
        "get_recent_deployments", "get_release_diff", "get_config_changes", "get_deployment_status");

    public override string Name => AgentNames.Deployment;

    protected override AgentDefinition Definition => AgentDefinition;
}
