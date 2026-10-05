using ProductionIncident.Agents.Prompts;
using ProductionIncident.Agents.Runtime;
using ProductionIncident.Core.Contracts;

namespace ProductionIncident.Agents.Supervisor;

public sealed class SupervisorAgent(AgentRunner runner)
{
    public static readonly AgentDefinition Definition = new(
        AgentNames.Supervisor,
        "supervisor@1.0.0",
        """
        You are the SupervisorAgent. Root-cause confidence is below the threshold.
        Decide which specialists run in the next round (the first one starts a handoff deep dive),
        avoid unnecessary agents, and decide when a human must take over.
        Specialists: LogsAgent, DatabaseAgent, MetricsAgent, DeploymentAgent.
        """,
        "supervise-investigation",
        [],
        OutputContracts.Supervisor);

    public async Task<(SupervisorDecision Decision, AgentRunResult<SupervisorDecision> Run)> DecideAsync(
        string incidentId, string contextJson, CancellationToken cancellationToken)
    {
        var run = await runner.RunAsync<SupervisorDecision>(new AgentRunRequest
        {
            IncidentId = incidentId,
            Agent = Definition,
            UserPrompt = PromptBuilder.BuildUserPrompt(AgentModes.Supervise, null, contextJson),
            MaxIterations = 2,
        }, cancellationToken).ConfigureAwait(false);

        var output = run.Output;
        var agents = (output?.AgentsToRun ?? [])
            .Select(AgentNames.NormalizeSpecialist)
            .OfType<string>()
            .Distinct()
            .ToList();

        var decision = new SupervisorDecision(
            agents,
            output?.Reason ?? $"Supervisor unavailable ({run.Status}): {run.Error}",
            output?.InvestigationComplete ?? false);

        return (decision, run);
    }
}
