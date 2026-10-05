using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ProductionIncident.Core.Contracts;
using ProductionIncident.Core.State;
using ProductionIncident.Core.Tools;
using ProductionIncident.Harness;
using ProductionIncident.Harness.Approvals;
using ProductionIncident.Harness.Budgets;
using ProductionIncident.Harness.Context;

namespace ProductionIncident.UnitTests.Harness;

public sealed class HarnessTests
{
    private static ToolDescriptor Tool(string name, ToolRisk risk) =>
        new(name, "test", risk, AIFunctionFactory.Create(() => "ok", name));

    [Fact]
    public void Read_only_tools_are_automatic_and_production_changing_tools_need_approval()
    {
        var policy = new ApprovalPolicy(Options.Create(new HarnessOptions()));

        Assert.Equal(ToolDecision.Allow, policy.Decide(Tool("get_error_rate", ToolRisk.ReadOnly)));
        Assert.Equal(ToolDecision.RequireApproval, policy.Decide(Tool("rollback_deployment", ToolRisk.ProductionChanging)));
        Assert.Equal(ToolDecision.RequireApproval, policy.Decide(null)); // unknown → deny by default
    }

    [Fact]
    public void Auto_approved_list_is_explicit_opt_in()
    {
        var policy = new ApprovalPolicy(Options.Create(new HarnessOptions { AutoApprovedTools = ["restart_service"] }));

        Assert.Equal(ToolDecision.Allow, policy.Decide(Tool("restart_service", ToolRisk.ProductionChanging)));
        Assert.Equal(ToolDecision.RequireApproval, policy.Decide(Tool("delete_resource", ToolRisk.ProductionChanging)));
    }

    [Fact]
    public void Budget_tracker_throws_once_token_budget_is_exhausted()
    {
        var budget = new BudgetTracker(Options.Create(new HarnessOptions { MaxTokensPerIncident = 1_000 }));

        budget.Record("INC-1", 600, 300, 0.01m);
        budget.EnsureWithinBudget("INC-1");
        budget.Record("INC-1", 100, 50, 0.01m);

        Assert.Throws<BudgetExceededException>(() => budget.EnsureWithinBudget("INC-1"));
        budget.EnsureWithinBudget("INC-2"); // budgets are per incident
    }

    [Fact]
    public void Trimmer_keeps_recent_tool_results_and_summarizes_older_ones()
    {
        var trimmer = new ToolResultTrimmer(Options.Create(new HarnessOptions { KeepRecentToolResults = 2, TrimmedPreviewChars = 10, MaxToolResultChars = 50 }));
        var big = new string('x', 200);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Tool, [new FunctionResultContent("c1", big)]),
            new(ChatRole.Tool, [new FunctionResultContent("c2", big)]),
            new(ChatRole.Tool, [new FunctionResultContent("c3", big)]),
        };

        var trimmed = trimmer.TrimHistory(messages, new Dictionary<string, string> { ["c1"] = "ws://1" });

        Assert.Equal(1, trimmed);
        Assert.StartsWith("[trimmed:", messages[0].Contents.OfType<FunctionResultContent>().Single().Result?.ToString());
        Assert.Contains("ws://1", messages[0].Contents.OfType<FunctionResultContent>().Single().Result?.ToString());
        Assert.Equal(big, messages[2].Contents.OfType<FunctionResultContent>().Single().Result?.ToString());

        var arrival = trimmer.TrimOnArrival(big, "ws://x");
        Assert.True(arrival.Length < big.Length);
        Assert.Contains("ws://x", arrival);
    }

    [Fact]
    public void Compactor_exposes_facts_questions_and_hypothesis_without_raw_dumps()
    {
        var state = new InvestigationState { IncidentId = "INC-1", Title = "t", Service = "checkout-api" };
        state.AgentResults.Add(new AgentEvidence("LogsAgent",
            [new Evidence("log", "1843x SQL timeouts", "get_recent_errors"), new Evidence("log", "1843x SQL timeouts", "get_recent_errors")],
            [new Hypothesis("pool exhaustion", 0.7)],
            ["was the pool saturated?"]));
        state.OpenQuestions.Add("was the pool saturated?");
        state.CurrentRootCause = "pool exhaustion";

        var view = ContextCompactor.Compact(state);

        Assert.Single(view.EstablishedFacts, f => f.Contains("SQL timeouts")); // deduplicated
        Assert.Contains(view.Hypotheses, h => h.Contains("pool exhaustion"));
        Assert.Contains("was the pool saturated?", view.OpenQuestions);
        Assert.Equal("pool exhaustion", view.CurrentHypothesis);
    }
}
