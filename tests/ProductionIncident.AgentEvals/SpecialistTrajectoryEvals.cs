using ProductionIncident.Agents;
using ProductionIncident.Agents.Runtime;
using ProductionIncident.Agents.Specialists;
using ProductionIncident.Application.Knowledge;
using ProductionIncident.Agents.RootCause;
using ProductionIncident.Core.Contracts;
using ProductionIncident.Core.State;
using ProductionIncident.Core.Tools;
using ProductionIncident.Harness.Context;
using ProductionIncident.LlmOps.Evaluation;
using ProductionIncident.Tests.Shared;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace ProductionIncident.AgentEvals;

/// <summary>
/// Agent trajectory evals (blueprint §14.1): tool selection, unnecessary calls, handoff behaviour, output contract.
/// Run against the scripted model in CI; point AI:Provider at a real model to evaluate prompts/skills/models.
/// </summary>
public sealed class SpecialistTrajectoryEvals
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Context(string service, string title) =>
        ContextCompactor.ToPrompt(new InvestigationState { IncidentId = "EVAL", Title = title, Service = service });

    private static ISpecialistAgent Specialist(TestSystem system, string name) =>
        system.Services.GetServices<ISpecialistAgent>().Single(s => s.Name == name);

    [Fact]
    public async Task LogsAgent_reads_errors_then_follows_the_sample_trace()
    {
        await using var system = TestSystem.Create();

        var result = await Specialist(system, AgentNames.Logs).InvestigateAsync(
            new SpecialistRequest("EVAL-1", Context("checkout-api", "checkout-api HTTP 500"), AgentModes.Broad, null, []), Ct);

        Assert.Equal(new[] { "get_recent_errors", "get_trace" }, result.Run.ToolCalls.Select(t => t.Name));
        Assert.Contains(result.Evidence.Evidence, e => e.Type == "trace" && e.Description.Contains("t-9f2a41"));
        Assert.Contains(result.Evidence.Hypotheses, h => h.Cause.Contains("pool exhaustion"));
        Assert.Null(result.Handoff); // broad mode never hands off
    }

    [Fact]
    public async Task DatabaseAgent_broad_checks_the_snapshot_and_flags_the_gap()
    {
        await using var system = TestSystem.Create();

        var result = await Specialist(system, AgentNames.Database).InvestigateAsync(
            new SpecialistRequest("EVAL-2", Context("checkout-api", "checkout-api HTTP 500"), AgentModes.Broad, null, []), Ct);

        Assert.DoesNotContain(result.Run.ToolCalls, t => t.Name == "get_connection_pool");
        Assert.Contains(result.Evidence.OpenQuestions, q => q.Contains("connection pool", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeploymentAgent_deep_dive_inspects_the_diff_and_hands_off_to_database()
    {
        await using var system = TestSystem.Create();

        var result = await Specialist(system, AgentNames.Deployment).InvestigateAsync(
            new SpecialistRequest("EVAL-3", Context("checkout-api", "checkout-api HTTP 500"), AgentModes.DeepDive, "Release diff v1.41 → v1.42", HandoffTopology.AllowedTargets(AgentNames.Deployment)), Ct);

        Assert.Contains(result.Run.ToolCalls, t => t.Name == "get_release_diff" && t.Arguments.Contains("v1.41") && t.Arguments.Contains("v1.42"));
        Assert.Equal(AgentNames.Database, result.Handoff?.Target);
        Assert.Contains(result.Evidence.Evidence, e => e.Type == "code-change");
    }

    [Theory]
    [InlineData(AgentNames.Logs)]
    [InlineData(AgentNames.Database)]
    [InlineData(AgentNames.Metrics)]
    [InlineData(AgentNames.Deployment)]
    public async Task Specialists_only_use_their_own_domain_tools_and_respect_the_contract(string agent)
    {
        await using var system = TestSystem.Create();
        var catalog = system.Get<IToolCatalog>();
        var allowedDomains = agent switch
        {
            AgentNames.Logs => "logs",
            AgentNames.Database => "database",
            AgentNames.Metrics => "metrics",
            _ => "deployment",
        };

        var result = await Specialist(system, agent).InvestigateAsync(
            new SpecialistRequest("EVAL-4", Context("search-api", "search-api timeouts"), AgentModes.Broad, null, []), Ct);

        Assert.True(result.Run.Succeeded, result.Run.Error);
        Assert.Equal(agent, result.Evidence.AgentName);
        Assert.NotEmpty(result.Evidence.Evidence);
        Assert.All(result.Evidence.Hypotheses, h => Assert.InRange(h.Confidence, 0, 1));
        foreach (var call in result.Run.ToolCalls)
        {
            var tool = await catalog.FindAsync(call.Name, Ct);
            Assert.Equal(allowedDomains, tool?.Domain);
        }

        Assert.True(result.Run.Iterations <= 8);
    }

    [Fact]
    public async Task RootCauseAgent_uses_knowledge_and_memory_and_cites_them()
    {
        await using var system = TestSystem.Create();
        var state = await system.InvestigateAsync("EVAL-5", "search-api latency spike and timeouts", "search-api", ct: Ct);
        var knowledge = system.Get<KnowledgeToolFactory>();
        var sink = new List<string>();

        var (analysis, run) = await system.Get<RootCauseAgent>().AnalyzeAsync(
            "EVAL-5b", ContextCompactor.ToPrompt(state), [knowledge.SearchKnowledge(), knowledge.RecallSimilarIncidents(sink)], sink, Ct);

        Assert.Equal(new[] { "search_knowledge", "recall_similar_incidents" }, run.ToolCalls.Select(t => t.Name));
        Assert.Contains("ep-INC-064", sink);
        Assert.Contains(analysis.SupportingEvidence, e => e.Contains("runbook:high-cpu-traffic-surge"));
        Assert.True(analysis.Confidence >= 0.85);
    }

    [Fact]
    public async Task Llm_judge_output_is_parseable()
    {
        await using var system = TestSystem.Create();
        var judge = new ChatClientLlmJudge(system.Get<IChatClient>());
        var testCase = new RegressionCase("J-1", new RegressionInput("t", "d", "svc", null), [], [], [], [], [], []);
        var outcome = new InvestigationOutcome("J-1", InvestigationPhase.Resolved, "cause", 0.9, ["e"], ["e"], [], "", 1, 0, 0, 0, 0);

        var score = await judge.JudgeAsync(testCase, outcome, Ct);

        Assert.InRange(score.Average, 0.0, 1.0);
        Assert.True(score.Average > 0);
    }
}
