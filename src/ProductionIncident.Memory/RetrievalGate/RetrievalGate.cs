using ProductionIncident.Core.Contracts;
using ProductionIncident.Core.State;

namespace ProductionIncident.Memory.RetrievalGate;

public sealed record RetrievalDecision(bool Retrieve, string Reason);

/// <summary>
/// Retrieval gate (blueprint §6.3): long-term memory is NOT retrieved on every turn — only when the task
/// can benefit from past incidents. Deterministic, so it is cheap and testable.
/// </summary>
public static class MemoryRetrievalGate
{
    public static RetrievalDecision ShouldRetrieve(string agentName, InvestigationState state)
    {
        switch (agentName)
        {
            case AgentNames.RootCause:
                if (!state.AgentResults.Any(r => r.Hypotheses.Count > 0))
                {
                    return new RetrievalDecision(false, "No hypotheses yet: nothing to compare with past incidents.");
                }

                if (state.Round > 1 && state.MemoryRetrieved.Count > 0 && state.RootCause?.MissingEvidence.Count == 0)
                {
                    return new RetrievalDecision(false, "Memory already retrieved and no missing evidence left.");
                }

                return new RetrievalDecision(true, "Root-cause analysis with hypotheses: similar past incidents may calibrate confidence.");

            case AgentNames.Remediation:
                return new RetrievalDecision(state.RootCauseConfidence > 0, "Past resolutions of similar incidents inform mitigation.");

            default:
                // Triage, Supervisor and specialists work on live state only.
                return new RetrievalDecision(false, $"{agentName} works on live production state only.");
        }
    }
}
