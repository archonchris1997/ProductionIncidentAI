using ProductionIncident.Application.Incidents;
using ProductionIncident.Core.Abstractions;
using ProductionIncident.Core.Contracts;
using ProductionIncident.Core.Json;
using ProductionIncident.Core.State;
using ProductionIncident.Tests.Shared;

namespace ProductionIncident.WorkflowTests;

public sealed class IncidentWorkflowTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Connection_leak_concurrent_round_contradiction_handoff_deep_dive_approval_and_learning()
    {
        await using var system = TestSystem.Create();

        // Phases A–H: runs until a human must approve the production change.
        var state = await system.InvestigateAsync("INC-123", "checkout-api HTTP 500 after deployment", "checkout-api", ct: Ct);

        Assert.Equal(InvestigationPhase.AwaitingApproval, state.Phase);

        // Round 1 (concurrent) found a contradiction: logs say pool exhaustion, DB says healthy (current snapshot).
        Assert.Contains(state.Timeline, t => t.Message.StartsWith("Conflict detected", StringComparison.Ordinal));
        Assert.True(state.RootCauseHistory[0].Confidence < 0.85);
        Assert.NotEmpty(state.RootCauseHistory[0].Contradictions);

        // Round 2 (deep dive): supervisor starts with DeploymentAgent, which hands off to DatabaseAgent.
        Assert.Equal(2, state.Round);
        var handoff = Assert.Single(state.Handoffs);
        Assert.Equal((AgentNames.Deployment, AgentNames.Database), (handoff.From, handoff.To));

        // Root cause confirmed with evidence.
        Assert.True(state.RootCauseConfidence >= 0.85);
        Assert.Contains("v1.42", state.CurrentRootCause);
        Assert.Contains("connection leak", state.CurrentRootCause, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(state.Conflicts);
        Assert.NotEmpty(state.MemoryRetrieved); // retrieval gate allowed episodic recall (INC-087)

        // Remediation proposed but NOT executed before approval.
        var approval = Assert.Single(await system.Approvals.ListAsync("INC-123", ApprovalStatus.Pending, Ct));
        Assert.Equal("rollback_deployment", approval.Action.Tool);
        Assert.Empty(system.Production.ExecutedOperations);

        // Human approves → action → verification → close & learn.
        state = await system.DecideAllAsync("INC-123", approve: true, Ct);

        Assert.Equal(InvestigationPhase.Resolved, state.Phase);
        Assert.True(state.Verified);
        var executed = Assert.Single(system.Production.ExecutedOperations);
        Assert.Equal(("rollback_deployment", "checkout-api"), (executed.Operation, executed.Target));
        Assert.Contains(state.MemoryStored, m => m.Contains("ep-INC-123"));
        Assert.All(state.Todos, t => Assert.True(t.Status is TodoStatus.Done or TodoStatus.Skipped, $"{t.Title} is {t.Status}"));

        // Postmortem indexed into RAG, episode stored in long-term memory.
        var hits = await system.Get<IKnowledgeBase>().SearchAsync("checkout-api v1.42 connection leak", 5, "postmortem", Ct);
        Assert.Contains(hits, h => h.SourceId == "postmortem:INC-123");
        var episodes = await system.Get<ILongTermMemory>().ListEpisodesAsync(Ct);
        Assert.Contains(episodes, e => e.IncidentId == "INC-123");

        // Auditability: every agent run has a receipt.
        var agents = system.Receipts.List("INC-123").Select(r => r.Agent).ToHashSet();
        Assert.Superset(new HashSet<string> { AgentNames.Triage, AgentNames.Logs, AgentNames.Database, AgentNames.Metrics, AgentNames.Deployment, AgentNames.RootCause, AgentNames.Supervisor, AgentNames.Remediation, "IncidentWorkflow" }, agents);
        Assert.Contains("# INC-123", state.FinalReport);
    }

    [Fact]
    public async Task Cpu_saturation_is_resolved_in_one_round_by_scaling_out()
    {
        await using var system = TestSystem.Create();

        var state = await system.InvestigateAsync("INC-124", "search-api latency spike and timeouts", "search-api", ct: Ct);

        Assert.Equal(1, state.Round);
        Assert.Empty(state.Handoffs);
        Assert.Contains("traffic surge", state.CurrentRootCause);
        var approval = Assert.Single(await system.Approvals.ListAsync("INC-124", ApprovalStatus.Pending, Ct));
        Assert.Equal("scale_service", approval.Action.Tool);
        Assert.Equal("12", LlmJson.ArgToString(approval.Action.Arguments["replicas"]));

        state = await system.DecideAllAsync("INC-124", approve: true, Ct);
        Assert.Equal(InvestigationPhase.Resolved, state.Phase);
    }

    [Fact]
    public async Task Bad_configuration_change_is_reverted()
    {
        await using var system = TestSystem.Create();

        var state = await system.InvestigateAsync("INC-125", "payments-api failing with HTTP 502", "payments-api", ct: Ct);

        Assert.Contains("PaymentGateway:TimeoutMs", state.CurrentRootCause);
        var approval = Assert.Single(await system.Approvals.ListAsync("INC-125", ApprovalStatus.Pending, Ct));
        Assert.Equal("change_configuration", approval.Action.Tool);
        Assert.Equal("5000", LlmJson.ArgToString(approval.Action.Arguments["value"]));

        state = await system.DecideAllAsync("INC-125", approve: true, Ct);
        Assert.Equal(InvestigationPhase.Resolved, state.Phase);
    }

    [Fact]
    public async Task Rejected_remediation_escalates_without_touching_production()
    {
        await using var system = TestSystem.Create();
        await system.InvestigateAsync("INC-126", "checkout-api HTTP 500 after deployment", "checkout-api", ct: Ct);

        var state = await system.DecideAllAsync("INC-126", approve: false, Ct);

        Assert.Equal(InvestigationPhase.Escalated, state.Phase);
        Assert.Contains("rejected", state.StopReason);
        Assert.Empty(system.Production.ExecutedOperations);
        Assert.NotNull(state.FinalReport);
    }

    [Fact]
    public async Task Max_rounds_reached_escalates_to_a_human()
    {
        await using var system = TestSystem.Create(new Dictionary<string, string?> { ["Harness:MaxInvestigationRounds"] = "1" });

        var state = await system.InvestigateAsync("INC-127", "checkout-api HTTP 500 after deployment", "checkout-api", ct: Ct);

        Assert.Equal(InvestigationPhase.Escalated, state.Phase);
        Assert.Contains("Max investigation rounds", state.StopReason);
        Assert.Empty(await system.Approvals.ListAsync("INC-127", null, Ct));
    }

    [Fact]
    public async Task Token_budget_stops_the_investigation()
    {
        await using var system = TestSystem.Create(new Dictionary<string, string?> { ["Harness:MaxTokensPerIncident"] = "500" });

        var state = await system.InvestigateAsync("INC-128", "checkout-api HTTP 500 after deployment", "checkout-api", ct: Ct);

        Assert.Equal(InvestigationPhase.Escalated, state.Phase);
        Assert.Contains("budget", state.StopReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Kill_switch_stops_a_waiting_investigation()
    {
        await using var system = TestSystem.Create();
        await system.InvestigateAsync("INC-129", "checkout-api HTTP 500 after deployment", "checkout-api", ct: Ct);

        var state = await system.Workflow.StopAsync("INC-129", "handled manually", Ct);

        Assert.Equal(InvestigationPhase.Stopped, state!.Phase);
        state = await system.DecideAllAsync("INC-129", approve: true, Ct);
        Assert.Equal(InvestigationPhase.Stopped, state.Phase);
        Assert.Empty(system.Production.ExecutedOperations);
    }

    [Fact]
    public async Task Ingestion_is_idempotent()
    {
        await using var system = TestSystem.Create();
        var request = new IncidentRequest { IncidentId = "INC-130", Title = "checkout-api HTTP 500", Service = "checkout-api" };

        var first = await system.Workflow.StartAsync(request, Ct);
        var second = await system.Workflow.StartAsync(request with { Title = "duplicate alert" }, Ct);

        Assert.Equal(first.IncidentId, second.IncidentId);
        Assert.Equal("checkout-api HTTP 500", second.Title);
        Assert.Single(await system.Store.ListAsync(Ct));
    }

    [Fact]
    public async Task Interrupted_investigation_resumes_from_checkpoint_after_restart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pi-tests", Guid.NewGuid().ToString("N"));
        var settings = new Dictionary<string, string?> { ["Persistence:Provider"] = "File", ["Persistence:Directory"] = directory };
        try
        {
            // Process 1: incident accepted and checkpointed, then the process "dies" before running it.
            await using (var first = TestSystem.Create(settings))
            {
                await first.Workflow.StartAsync(new IncidentRequest { IncidentId = "INC-131", Title = "search-api timeouts", Service = "search-api" }, Ct);
            }

            // Process 2: finds the checkpoint and resumes.
            await using var second = TestSystem.Create(settings);
            Assert.Equal(1, await second.Workflow.ResumeInterruptedAsync(Ct));

            var state = await second.Workflow.RunAsync("INC-131", Ct);
            Assert.Equal(InvestigationPhase.AwaitingApproval, state!.Phase);
            Assert.True(File.Exists(Path.Combine(directory, "INC-131.json")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
