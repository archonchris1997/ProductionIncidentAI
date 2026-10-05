using System.Text.Json;
using Microsoft.Extensions.AI;
using ProductionIncident.Core.Json;
using ProductionIncident.Core.State;
using ProductionIncident.LlmOps.Evaluation;
using ProductionIncident.LlmOps.ReleaseGates;
using ProductionIncident.Tests.Shared;

namespace ProductionIncident.RegressionEvals;

/// <summary>
/// Release gate (blueprint §14.3): every prompt / skill / model change runs the regression dataset end-to-end,
/// is scored with deterministic evals + LLM-as-judge, and is blocked if thresholds are not met.
/// </summary>
public sealed class RegressionSuite
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static IReadOnlyList<RegressionCase> LoadDataset()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "evals", "datasets", "regression.json");
        return JsonSerializer.Deserialize<List<RegressionCase>>(File.ReadAllText(path), JsonDefaults.Options)!;
    }

    public static TheoryData<string> CaseIds()
    {
        var data = new TheoryData<string>();
        foreach (var c in LoadDataset())
        {
            data.Add(c.Id);
        }

        return data;
    }

    private static async Task<(EvalResult Result, JudgeScore Judge)> RunCaseAsync(RegressionCase testCase)
    {
        await using var system = TestSystem.Create();
        var state = await system.InvestigateAsync(testCase.Id, testCase.Input.Title, testCase.Input.Service ?? "", testCase.Input.Description, Ct);
        if (state.Phase == InvestigationPhase.AwaitingApproval)
        {
            state = await system.DecideAllAsync(testCase.Id, approve: true, Ct);
        }

        var outcome = InvestigationOutcome.From(state, system.Receipts.List(testCase.Id));
        var result = DeterministicEvaluator.Evaluate(testCase, outcome);
        var judge = await new ChatClientLlmJudge(system.Get<IChatClient>()).JudgeAsync(testCase, outcome, Ct);
        return (result, judge);
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public async Task Regression_case_passes_deterministic_evals(string caseId)
    {
        var testCase = LoadDataset().Single(c => c.Id == caseId);

        var (result, _) = await RunCaseAsync(testCase);

        TestContext.Current.TestOutputHelper?.WriteLine(JsonDefaults.Serialize(new { result.CaseId, result.Scores, result.Failures, result.Outcome.RootCause, result.Outcome.Confidence }, indented: true));
        Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
        Assert.Equal(1.0, result.Scores[DeterministicEvaluator.Metrics.ToolPrecision]);
        Assert.Equal(1.0, result.Scores[DeterministicEvaluator.Metrics.EvidenceCoverage]);
        Assert.Equal(InvestigationPhase.Resolved, result.Outcome.Phase);
    }

    [Fact]
    public async Task Release_gate_passes_for_the_current_prompts_skills_and_model()
    {
        var results = new List<EvalResult>();
        var judges = new Dictionary<string, JudgeScore>();
        foreach (var testCase in LoadDataset())
        {
            var (result, judge) = await RunCaseAsync(testCase);
            results.Add(result);
            judges[testCase.Id] = judge;
        }

        var gate = ReleaseGate.Evaluate(results, judges, new ReleaseGateThresholds());

        TestContext.Current.TestOutputHelper?.WriteLine(JsonDefaults.Serialize(gate, indented: true));
        Assert.True(gate.Passed, string.Join(Environment.NewLine, gate.Reasons));
    }

    [Fact]
    public void Release_gate_blocks_a_confidently_wrong_model()
    {
        var testCase = LoadDataset().First();
        var wrong = new InvestigationOutcome(
            testCase.Id, InvestigationPhase.Resolved,
            RootCause: "CPU saturation caused by a traffic surge",
            Confidence: 0.95,
            SupportingEvidence: [], Evidence: ["CPU 41%"], ToolsCalled: ["get_cpu", "restart_everything"],
            RemediationText: "scale_service", Rounds: 1, Handoffs: 0, TotalTokens: 1000, TotalCostUsd: 0.01m, TotalLatencyMs: 10);

        var result = DeterministicEvaluator.Evaluate(testCase, wrong);
        var gate = ReleaseGate.Evaluate([result], new Dictionary<string, JudgeScore>(), new ReleaseGateThresholds());

        Assert.False(result.Passed);
        Assert.Equal(0, result.Scores[DeterministicEvaluator.Metrics.ConfidenceCalibration]); // confidently wrong
        Assert.Equal(0, result.Scores[DeterministicEvaluator.Metrics.ForbiddenClaimsOk]);
        Assert.True(result.Scores[DeterministicEvaluator.Metrics.ToolPrecision] < 1);
        Assert.False(gate.Passed);
        Assert.Contains(gate.Reasons, r => r.Contains("forbidden-claim"));
    }
}
