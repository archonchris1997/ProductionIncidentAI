namespace ProductionIncident.Core.Contracts;

// Structured contracts exchanged between agents (blueprint §8).
// Agents never talk to each other in free text: every result is one of these records.

public sealed record Evidence(
    string Type,
    string Description,
    string Source);

public sealed record Hypothesis(
    string Cause,
    double Confidence);

public sealed record AgentEvidence(
    string AgentName,
    IReadOnlyList<Evidence> Evidence,
    IReadOnlyList<Hypothesis> Hypotheses,
    IReadOnlyList<string> OpenQuestions)
{
    public static AgentEvidence Failed(string agentName, string reason) =>
        new(agentName, [], [], [$"{agentName} could not complete: {reason}"]);
}

public sealed record RootCauseAnalysis(
    string Cause,
    double Confidence,
    IReadOnlyList<string> SupportingEvidence,
    IReadOnlyList<string> Contradictions,
    IReadOnlyList<string> MissingEvidence);

public sealed record SupervisorDecision(
    IReadOnlyList<string> AgentsToRun,
    string Reason,
    bool InvestigationComplete);

public sealed record TriageResult(
    string Category,
    string Severity,
    IReadOnlyList<string> AffectedServices,
    IReadOnlyList<string> AgentsToRun,
    string Strategy,
    string Summary);

/// <summary>A production-changing action proposed by an agent. Never executed without approval.</summary>
public sealed record ProposedAction(
    string Tool,
    IReadOnlyDictionary<string, object?> Arguments,
    string Risk,
    string Rationale);

public sealed record RemediationPlan(
    IReadOnlyList<ProposedAction> ImmediateMitigation,
    IReadOnlyList<string> PermanentFix,
    IReadOnlyList<string> VerificationPlan,
    IReadOnlyList<string> RollbackPlan);

public sealed record HandoffRecord(
    int Round,
    string From,
    string To,
    string Reason);
