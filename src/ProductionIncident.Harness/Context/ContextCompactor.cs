using ProductionIncident.Core.Json;
using ProductionIncident.Core.State;

namespace ProductionIncident.Harness.Context;

public sealed record CompactInvestigationView(
    string IncidentId,
    string Title,
    string Description,
    string? Service,
    int Round,
    IReadOnlyList<string> EstablishedFacts,
    IReadOnlyList<string> Hypotheses,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> OpenQuestions,
    string? CurrentHypothesis,
    double Confidence,
    IReadOnlyList<string> MissingEvidence,
    IReadOnlyList<string> NextActions);

/// <summary>
/// Context compaction (blueprint §13.2): agents never receive the raw state (tool dumps, transcripts),
/// only established facts, unresolved questions, the current hypothesis and next actions.
/// Deterministic on purpose: no LLM call, no information invented.
/// </summary>
public static class ContextCompactor
{
    public static CompactInvestigationView Compact(InvestigationState state, int maxFacts = 30)
    {
        var facts = state.AgentResults
            .SelectMany(r => r.Evidence.Select(e => (r.AgentName, e)))
            .DistinctBy(x => $"{x.e.Type}|{x.e.Description}".ToLowerInvariant())
            .Select(x => $"[{x.AgentName}] ({x.e.Type}) {x.e.Description} — source: {x.e.Source}")
            .TakeLast(maxFacts)
            .ToList();

        var hypotheses = state.AgentResults
            .SelectMany(r => r.Hypotheses.Select(h => (r.AgentName, h)))
            .OrderByDescending(x => x.h.Confidence)
            .Select(x => $"{x.h.Cause} (confidence {x.h.Confidence:0.00}, by {x.AgentName})")
            .Distinct()
            .Take(12)
            .ToList();

        var next = state.Todos
            .Where(t => t.Status is TodoStatus.Pending or TodoStatus.InProgress)
            .Select(t => t.Title)
            .ToList();

        return new CompactInvestigationView(
            state.IncidentId,
            state.Title,
            state.Description,
            state.Service,
            state.Round,
            facts,
            hypotheses,
            state.Conflicts.Distinct().ToList(),
            state.OpenQuestions.Distinct().TakeLast(15).ToList(),
            state.CurrentRootCause,
            state.RootCauseConfidence,
            state.RootCause?.MissingEvidence ?? [],
            next);
    }

    public static string ToPrompt(InvestigationState state, int maxFacts = 30) =>
        JsonDefaults.Serialize(Compact(state, maxFacts), indented: true);
}
