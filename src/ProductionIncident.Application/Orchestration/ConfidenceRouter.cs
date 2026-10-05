using ProductionIncident.Core.Contracts;
using ProductionIncident.Harness;

namespace ProductionIncident.Application.Orchestration;

public enum RouteKind
{
    Remediate,
    DeepDive,
    Escalate,
}

public sealed record RouteDecision(RouteKind Kind, string Reason);

/// <summary>
/// Deterministic confidence router (blueprint Phase F + §11 termination conditions).
/// The LLM proposes a confidence; code decides what happens with it.
/// </summary>
public static class ConfidenceRouter
{
    public static RouteDecision Route(RootCauseAnalysis analysis, int round, int newEvidenceLastRound, HarnessOptions options)
    {
        if (analysis.Confidence >= options.ConfidenceThreshold && analysis.Contradictions.Count == 0)
        {
            return new RouteDecision(RouteKind.Remediate, $"Confidence {analysis.Confidence:0.00} ≥ {options.ConfidenceThreshold:0.00} with no unresolved contradiction.");
        }

        if (round >= options.MaxInvestigationRounds)
        {
            return new RouteDecision(RouteKind.Escalate, $"Max investigation rounds ({options.MaxInvestigationRounds}) reached at confidence {analysis.Confidence:0.00}.");
        }

        if (round > 1 && newEvidenceLastRound == 0)
        {
            return new RouteDecision(RouteKind.Escalate, "No meaningful new evidence found in the last round.");
        }

        var why = analysis.Contradictions.Count > 0
            ? $"{analysis.Contradictions.Count} unresolved contradiction(s)"
            : $"confidence {analysis.Confidence:0.00} < {options.ConfidenceThreshold:0.00}";
        return new RouteDecision(RouteKind.DeepDive, $"Deep dive needed: {why}.");
    }
}
