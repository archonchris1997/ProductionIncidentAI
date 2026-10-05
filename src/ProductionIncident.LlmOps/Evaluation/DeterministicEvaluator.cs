namespace ProductionIncident.LlmOps.Evaluation;

/// <summary>
/// Deterministic output + trajectory evals (blueprint §14.1). Cheap, reproducible, run on every change.
/// </summary>
public static class DeterministicEvaluator
{
    public static class Metrics
    {
        public const string RootCauseCorrect = "root_cause_correct";
        public const string EvidenceCoverage = "evidence_coverage";
        public const string ForbiddenClaimsOk = "forbidden_claims_ok";
        public const string ToolRecall = "tool_recall";
        public const string ToolPrecision = "tool_precision";
        public const string ResolutionMatch = "resolution_match";
        public const string ConfidenceCalibration = "confidence_calibration";
        public const string RoundsWithinLimit = "rounds_within_limit";
    }

    public static EvalResult Evaluate(RegressionCase testCase, InvestigationOutcome outcome)
    {
        var failures = new List<string>();
        var scores = new Dictionary<string, double>();
        var cause = outcome.RootCause ?? "";

        // Output evals
        var correct = testCase.ExpectedRootCause.All(k => cause.Contains(k, StringComparison.OrdinalIgnoreCase));
        scores[Metrics.RootCauseCorrect] = correct ? 1 : 0;
        if (!correct)
        {
            failures.Add($"Root cause '{cause}' does not contain [{string.Join(", ", testCase.ExpectedRootCause)}].");
        }

        var corpus = string.Join("\n", outcome.Evidence.Concat(outcome.SupportingEvidence));
        var covered = testCase.RequiredEvidence.Count(k => corpus.Contains(k, StringComparison.OrdinalIgnoreCase));
        scores[Metrics.EvidenceCoverage] = testCase.RequiredEvidence.Count == 0 ? 1 : (double)covered / testCase.RequiredEvidence.Count;
        foreach (var missing in testCase.RequiredEvidence.Where(k => !corpus.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add($"Required evidence not found: '{missing}'.");
        }

        var violations = testCase.ForbiddenClaims.Where(f => cause.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
        scores[Metrics.ForbiddenClaimsOk] = violations.Count == 0 ? 1 : 0;
        failures.AddRange(violations.Select(v => $"Forbidden claim made: '{v}'."));

        var resolutionOk = testCase.ExpectedResolution.All(k => outcome.RemediationText.Contains(k, StringComparison.OrdinalIgnoreCase));
        scores[Metrics.ResolutionMatch] = resolutionOk ? 1 : 0;
        if (!resolutionOk)
        {
            failures.Add($"Remediation does not contain [{string.Join(", ", testCase.ExpectedResolution)}].");
        }

        var confident = outcome.Confidence >= testCase.MinConfidence;
        scores[Metrics.ConfidenceCalibration] = (correct, confident) switch
        {
            (true, true) => 1.0,
            (false, false) => 1.0, // wrong but appropriately unsure
            (true, false) => 0.5,  // right but under-confident
            (false, true) => 0.0,  // confidently wrong: worst case
        };

        // Trajectory evals
        var called = new HashSet<string>(outcome.ToolsCalled, StringComparer.OrdinalIgnoreCase);
        var expected = testCase.ExpectedTools;
        scores[Metrics.ToolRecall] = expected.Count == 0 ? 1 : (double)expected.Count(called.Contains) / expected.Count;
        foreach (var tool in expected.Where(t => !called.Contains(t)))
        {
            failures.Add($"Expected tool not called: '{tool}'.");
        }

        var allowed = new HashSet<string>(expected.Concat(testCase.OptionalTools), StringComparer.OrdinalIgnoreCase);
        scores[Metrics.ToolPrecision] = called.Count == 0 ? 1 : (double)called.Count(allowed.Contains) / called.Count;

        var roundsOk = outcome.Rounds <= testCase.MaxRounds;
        scores[Metrics.RoundsWithinLimit] = roundsOk ? 1 : 0;
        if (!roundsOk)
        {
            failures.Add($"Used {outcome.Rounds} rounds (max {testCase.MaxRounds}).");
        }

        var passed = correct && violations.Count == 0 && resolutionOk && roundsOk;
        return new EvalResult(testCase.Id, scores, passed, failures, outcome);
    }
}
