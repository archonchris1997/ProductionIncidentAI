using ProductionIncident.Core.State;
using ProductionIncident.Core.Text;
using ProductionIncident.LlmOps.Receipts;

namespace ProductionIncident.Application.Workflows;

/// <summary>
/// Reference-free evals computed for every production run (no ground truth available):
/// groundedness heuristic, trajectory efficiency and operational cost/latency.
/// Offline evals against the regression dataset live in LlmOps.Evaluation.
/// </summary>
public static class OnlineEvaluator
{
    public static Dictionary<string, double> Score(InvestigationState state, IReadOnlyList<TurnReceipt> receipts)
    {
        var evidence = string.Join("\n", state.AllEvidence().Select(e => e.Description));
        var supporting = state.RootCause?.SupportingEvidence ?? [];
        var grounded = supporting.Count == 0
            ? 0
            : supporting.Count(s => TextSearch.Overlap(s, evidence) >= 0.4 || s.Contains("runbook", StringComparison.OrdinalIgnoreCase) || s.Contains("INC-", StringComparison.Ordinal)) / (double)supporting.Count;

        var toolCalls = receipts.Sum(r => r.ToolCalls.Count);
        var failedRuns = receipts.Count(r => r.FinalStatus != "Completed");

        return new Dictionary<string, double>
        {
            ["groundedness_heuristic"] = Math.Round(grounded, 3),
            ["root_cause_confidence"] = state.RootCauseConfidence,
            ["rounds"] = state.Round,
            ["handoffs"] = state.Handoffs.Count,
            ["agent_runs"] = receipts.Count,
            ["tool_calls"] = toolCalls,
            ["blocked_tool_calls"] = receipts.Sum(r => r.BlockedToolCalls.Count),
            ["failed_agent_runs"] = failedRuns,
            ["tokens_total"] = receipts.Sum(r => r.TokensIn + r.TokensOut),
            ["cost_usd"] = (double)receipts.Sum(r => r.CostUsd),
            ["latency_ms_total"] = Math.Round(receipts.Sum(r => r.LatencyMs), 1),
        };
    }
}
