using ProductionIncident.Agents.Prompts;
using ProductionIncident.Agents.Runtime;
using ProductionIncident.Core.Contracts;

namespace ProductionIncident.Agents.Triage;

public sealed class TriageAgent(AgentRunner runner)
{
    public static readonly AgentDefinition Definition = new(
        AgentNames.Triage,
        "triage@1.0.0",
        """
        You are the TriageAgent. You classify a new production incident, identify the affected services
        and choose the first investigation strategy. You do not investigate yourself.
        """,
        "triage-incident",
        [],
        OutputContracts.Triage);

    public async Task<(TriageResult Result, AgentRunResult<TriageResult> Run)> TriageAsync(
        string incidentId, string contextJson, string? knownService, CancellationToken cancellationToken)
    {
        var run = await runner.RunAsync<TriageResult>(new AgentRunRequest
        {
            IncidentId = incidentId,
            Agent = Definition,
            UserPrompt = PromptBuilder.BuildUserPrompt(AgentModes.Triage, null, contextJson),
            MaxIterations = 2,
        }, cancellationToken).ConfigureAwait(false);

        var output = run.Output;

        // Deterministic guard-rails around the model's choice.
        var agents = (output?.AgentsToRun ?? [])
            .Select(AgentNames.NormalizeSpecialist)
            .OfType<string>()
            .Distinct()
            .ToList();
        if (agents.Count == 0)
        {
            agents = [.. AgentNames.Specialists];
        }

        var services = (output?.AffectedServices ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToLowerInvariant()).Distinct().ToList();
        if (services.Count == 0 && !string.IsNullOrWhiteSpace(knownService))
        {
            services.Add(knownService);
        }

        var result = new TriageResult(
            Category: output?.Category ?? "other",
            Severity: output?.Severity ?? "SEV2",
            AffectedServices: services,
            AgentsToRun: agents,
            Strategy: output?.Strategy ?? "broad",
            Summary: output?.Summary ?? $"Triage fallback ({run.Status}): broad investigation.");

        return (result, run);
    }
}
