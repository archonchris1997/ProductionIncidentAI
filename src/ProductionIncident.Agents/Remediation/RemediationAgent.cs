using ProductionIncident.Agents.Prompts;
using ProductionIncident.Agents.Runtime;
using ProductionIncident.Core.Contracts;
using ProductionIncident.Core.Tools;

namespace ProductionIncident.Agents.Remediation;

public sealed class RemediationAgent(AgentRunner runner)
{
    public static readonly AgentDefinition Definition = new(
        AgentNames.Remediation,
        "remediation@1.0.0",
        """
        You are the RemediationAgent. The root cause is confirmed. Propose an immediate mitigation, a permanent fix,
        a verification plan and a rollback plan. Every production-changing step is a proposed action that a human
        must approve: you have read-only tools only.
        Available production-changing tools (for proposals): rollback_deployment(service, targetVersion),
        restart_service(service), scale_service(service, replicas), change_configuration(service, key, value),
        kill_pod(service, pod), execute_write_sql(database, sql), delete_resource(resourceId).
        """,
        "remediation-planning",
        ["get_deployment_status", "get_recent_deployments", "get_config_changes"],
        OutputContracts.Remediation);

    public async Task<(RemediationPlan Plan, AgentRunResult<RemediationPlan> Run)> ProposeAsync(
        string incidentId,
        string contextJson,
        IReadOnlyList<ToolDescriptor> knowledgeTools,
        CancellationToken cancellationToken)
    {
        var run = await runner.RunAsync<RemediationPlan>(new AgentRunRequest
        {
            IncidentId = incidentId,
            Agent = Definition,
            UserPrompt = PromptBuilder.BuildUserPrompt(AgentModes.Remediation, null, contextJson),
            ExtraTools = knowledgeTools,
        }, cancellationToken).ConfigureAwait(false);

        var plan = run.Output is { } p
            ? p with
            {
                ImmediateMitigation = p.ImmediateMitigation ?? [],
                PermanentFix = p.PermanentFix ?? [],
                VerificationPlan = p.VerificationPlan ?? [],
                RollbackPlan = p.RollbackPlan ?? [],
            }
            : new RemediationPlan([], [$"RemediationAgent produced no plan ({run.Status}): {run.Error}"], [], []);

        return (plan, run);
    }
}
