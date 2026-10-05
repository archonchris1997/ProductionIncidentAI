using ProductionIncident.Agents.Prompts;
using ProductionIncident.Agents.Runtime;
using ProductionIncident.Core.Contracts;
using ProductionIncident.Core.Tools;

namespace ProductionIncident.Agents.RootCause;

public sealed class RootCauseAgent(AgentRunner runner)
{
    public static readonly AgentDefinition Definition = new(
        AgentNames.RootCause,
        "rootcause@1.0.0",
        """
        You are the RootCauseAgent. You aggregate the specialists' evidence, resolve contradictions with evidence
        (never by majority vote), use organizational knowledge (search_knowledge) and past incidents
        (recall_similar_incidents, when available) as context, and may run read-only live checks to validate
        unresolved facts. You produce a structured, calibrated root-cause analysis.
        """,
        "root-cause-analysis",
        ["get_connection_pool", "get_trace", "get_error_rate", "get_recent_deployments", "get_release_diff"],
        OutputContracts.RootCause);

    public async Task<(RootCauseAnalysis Analysis, AgentRunResult<RootCauseAnalysis> Run)> AnalyzeAsync(
        string incidentId,
        string contextJson,
        IReadOnlyList<ToolDescriptor> knowledgeTools,
        List<string> retrievedMemorySink,
        CancellationToken cancellationToken)
    {
        var request = new AgentRunRequest
        {
            IncidentId = incidentId,
            Agent = Definition,
            UserPrompt = PromptBuilder.BuildUserPrompt(AgentModes.RootCause, null, contextJson),
            ExtraTools = knowledgeTools,
            RetrievedMemory = retrievedMemorySink,
        };

        var run = await runner.RunAsync<RootCauseAnalysis>(request, cancellationToken).ConfigureAwait(false);

        var analysis = run.Output is { } o
            ? o with
            {
                Confidence = Math.Clamp(o.Confidence, 0, 1),
                SupportingEvidence = o.SupportingEvidence ?? [],
                Contradictions = o.Contradictions ?? [],
                MissingEvidence = o.MissingEvidence ?? [],
            }
            : new RootCauseAnalysis(
                "Undetermined",
                0,
                [],
                [],
                [$"RootCauseAgent produced no analysis ({run.Status}): {run.Error}"]);

        return (analysis, run);
    }
}
