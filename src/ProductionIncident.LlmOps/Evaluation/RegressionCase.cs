using ProductionIncident.Core.Json;
using ProductionIncident.Core.State;
using ProductionIncident.LlmOps.Receipts;

namespace ProductionIncident.LlmOps.Evaluation;

public sealed record RegressionInput(string Title, string Description, string? Service, string? Severity);

/// <summary>A known incident with its expected outcome (blueprint §14.2).</summary>
public sealed record RegressionCase(
    string Id,
    RegressionInput Input,
    IReadOnlyList<string> ExpectedRootCause,
    IReadOnlyList<string> RequiredEvidence,
    IReadOnlyList<string> ForbiddenClaims,
    IReadOnlyList<string> ExpectedTools,
    IReadOnlyList<string> OptionalTools,
    IReadOnlyList<string> ExpectedResolution,
    double MinConfidence = 0.85,
    int MaxRounds = 3);

/// <summary>What an investigation produced, flattened for scoring.</summary>
public sealed record InvestigationOutcome(
    string IncidentId,
    InvestigationPhase Phase,
    string? RootCause,
    double Confidence,
    IReadOnlyList<string> SupportingEvidence,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> ToolsCalled,
    string RemediationText,
    int Rounds,
    int Handoffs,
    long TotalTokens,
    decimal TotalCostUsd,
    double TotalLatencyMs)
{
    public static InvestigationOutcome From(InvestigationState state, IReadOnlyList<TurnReceipt> receipts) =>
        new(
            state.IncidentId,
            state.Phase,
            state.RootCause?.Cause ?? state.CurrentRootCause,
            state.RootCauseConfidence,
            state.RootCause?.SupportingEvidence ?? [],
            state.AllEvidence().Select(e => e.Description).ToList(),
            receipts.SelectMany(r => r.ToolCalls).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            state.Remediation is null ? "" : JsonDefaults.Serialize(state.Remediation),
            state.Round,
            state.Handoffs.Count,
            receipts.Sum(r => r.TokensIn + r.TokensOut),
            receipts.Sum(r => r.CostUsd),
            receipts.Sum(r => r.LatencyMs));
}

public sealed record EvalResult(
    string CaseId,
    IReadOnlyDictionary<string, double> Scores,
    bool Passed,
    IReadOnlyList<string> Failures,
    InvestigationOutcome Outcome);
