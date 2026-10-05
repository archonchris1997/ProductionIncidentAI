using ProductionIncident.LlmOps.Evaluation;

namespace ProductionIncident.LlmOps.ReleaseGates;

public sealed class ReleaseGateThresholds
{
    public const string SectionName = "LlmOps:ReleaseGate";

    public double MinRootCauseAccuracy { get; set; } = 1.0;
    public double MinEvidenceCoverage { get; set; } = 0.8;
    public double MinToolRecall { get; set; } = 0.8;
    public double MinCalibration { get; set; } = 0.75;
    public double MinJudgeScore { get; set; } = 0.7;
    public int MaxForbiddenClaimViolations { get; set; } = 0;
    public decimal MaxAverageCostUsd { get; set; } = 1.0m;
}

public sealed record ReleaseGateResult(bool Passed, IReadOnlyList<string> Reasons, IReadOnlyDictionary<string, double> Aggregates);

/// <summary>
/// New prompt / skill / model → regression dataset → deterministic evals + judge → thresholds → pass/reject (blueprint §14.3).
/// </summary>
public static class ReleaseGate
{
    public static ReleaseGateResult Evaluate(
        IReadOnlyList<EvalResult> results,
        IReadOnlyDictionary<string, JudgeScore> judgeScores,
        ReleaseGateThresholds thresholds)
    {
        var reasons = new List<string>();
        if (results.Count == 0)
        {
            return new ReleaseGateResult(false, ["Empty regression dataset."], new Dictionary<string, double>());
        }

        double Avg(string metric) => results.Average(r => r.Scores.GetValueOrDefault(metric));

        var aggregates = new Dictionary<string, double>
        {
            ["root_cause_accuracy"] = Avg(DeterministicEvaluator.Metrics.RootCauseCorrect),
            ["evidence_coverage"] = Avg(DeterministicEvaluator.Metrics.EvidenceCoverage),
            ["tool_recall"] = Avg(DeterministicEvaluator.Metrics.ToolRecall),
            ["tool_precision"] = Avg(DeterministicEvaluator.Metrics.ToolPrecision),
            ["confidence_calibration"] = Avg(DeterministicEvaluator.Metrics.ConfidenceCalibration),
            ["forbidden_claim_violations"] = results.Count(r => r.Scores.GetValueOrDefault(DeterministicEvaluator.Metrics.ForbiddenClaimsOk) < 1),
            ["avg_cost_usd"] = (double)results.Average(r => r.Outcome.TotalCostUsd),
            ["avg_latency_ms"] = results.Average(r => r.Outcome.TotalLatencyMs),
            ["judge_average"] = judgeScores.Count == 0 ? 1 : judgeScores.Values.Average(j => j.Average),
        };

        void Check(bool ok, string message)
        {
            if (!ok)
            {
                reasons.Add(message);
            }
        }

        Check(aggregates["root_cause_accuracy"] >= thresholds.MinRootCauseAccuracy, $"Root-cause accuracy {aggregates["root_cause_accuracy"]:0.00} < {thresholds.MinRootCauseAccuracy:0.00}");
        Check(aggregates["evidence_coverage"] >= thresholds.MinEvidenceCoverage, $"Evidence coverage {aggregates["evidence_coverage"]:0.00} < {thresholds.MinEvidenceCoverage:0.00}");
        Check(aggregates["tool_recall"] >= thresholds.MinToolRecall, $"Tool recall {aggregates["tool_recall"]:0.00} < {thresholds.MinToolRecall:0.00}");
        Check(aggregates["confidence_calibration"] >= thresholds.MinCalibration, $"Calibration {aggregates["confidence_calibration"]:0.00} < {thresholds.MinCalibration:0.00}");
        Check(aggregates["forbidden_claim_violations"] <= thresholds.MaxForbiddenClaimViolations, $"{aggregates["forbidden_claim_violations"]} forbidden-claim violation(s)");
        Check(aggregates["judge_average"] >= thresholds.MinJudgeScore, $"Judge score {aggregates["judge_average"]:0.00} < {thresholds.MinJudgeScore:0.00}");
        Check((decimal)aggregates["avg_cost_usd"] <= thresholds.MaxAverageCostUsd, $"Average cost ${aggregates["avg_cost_usd"]:0.0000} > ${thresholds.MaxAverageCostUsd}");

        return new ReleaseGateResult(reasons.Count == 0, reasons, aggregates);
    }
}
