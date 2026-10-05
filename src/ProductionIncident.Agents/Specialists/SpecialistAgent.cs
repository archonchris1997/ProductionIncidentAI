using ProductionIncident.Agents.Prompts;
using ProductionIncident.Agents.Runtime;
using ProductionIncident.Core.Contracts;

namespace ProductionIncident.Agents.Specialists;

public sealed record SpecialistRequest(
    string IncidentId,
    string ContextJson,
    string Mode,
    string? Focus,
    IReadOnlyList<string> HandoffTargets);

public sealed record SpecialistResult(AgentEvidence Evidence, HandoffRequest? Handoff, AgentRunResult<AgentEvidence> Run);

public interface ISpecialistAgent
{
    string Name { get; }

    Task<SpecialistResult> InvestigateAsync(SpecialistRequest request, CancellationToken cancellationToken);
}

/// <summary>Shared behaviour of the four investigation specialists: same contract, different tools and skill.</summary>
public abstract class SpecialistAgent(AgentRunner runner) : ISpecialistAgent
{
    public abstract string Name { get; }

    protected abstract AgentDefinition Definition { get; }

    protected static AgentDefinition Define(string name, string promptVersion, string instructions, string skill, params string[] tools) =>
        new(name, promptVersion, instructions, skill, tools, OutputContracts.AgentEvidence);

    public async Task<SpecialistResult> InvestigateAsync(SpecialistRequest request, CancellationToken cancellationToken)
    {
        var extra = request.Mode == AgentModes.DeepDive
            ? "Deep-dive mode: follow the strongest clue for the focus above. If another specialist must continue, call handoff_to once."
            : "Broad mode: first concurrent round. Cover your domain quickly; do not hand off.";

        var run = await runner.RunAsync<AgentEvidence>(new AgentRunRequest
        {
            IncidentId = request.IncidentId,
            Agent = Definition,
            UserPrompt = PromptBuilder.BuildUserPrompt(request.Mode, request.Focus, request.ContextJson, extra),
            HandoffTargets = request.Mode == AgentModes.DeepDive ? request.HandoffTargets : [],
        }, cancellationToken).ConfigureAwait(false);

        // Deterministic post-conditions: the agent name is ours, not the model's; never trust it to self-report.
        var evidence = run.Output is { } output
            ? output with
            {
                AgentName = Name,
                Evidence = output.Evidence ?? [],
                Hypotheses = (output.Hypotheses ?? []).Select(h => h with { Confidence = Math.Clamp(h.Confidence, 0, 1) }).ToList(),
                OpenQuestions = output.OpenQuestions ?? [],
            }
            : AgentEvidence.Failed(Name, run.Error ?? run.Status.ToString());

        return new SpecialistResult(evidence, run.Handoff, run);
    }
}
