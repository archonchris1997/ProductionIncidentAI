using ProductionIncident.Agents;
using ProductionIncident.Application.Orchestration;
using ProductionIncident.Application.Workflows;
using ProductionIncident.Core.Contracts;
using ProductionIncident.Core.State;
using ProductionIncident.Harness;

namespace ProductionIncident.UnitTests.Orchestration;

public sealed class OrchestrationTests
{
    private static readonly HarnessOptions Options = new() { ConfidenceThreshold = 0.85, MaxInvestigationRounds = 3 };

    private static RootCauseAnalysis Rca(double confidence, params string[] contradictions) =>
        new("cause", confidence, ["e1"], contradictions, []);

    [Fact]
    public void High_confidence_without_contradictions_goes_to_remediation() =>
        Assert.Equal(RouteKind.Remediate, ConfidenceRouter.Route(Rca(0.9), 1, 5, Options).Kind);

    [Fact]
    public void High_confidence_with_unresolved_contradiction_still_deep_dives() =>
        Assert.Equal(RouteKind.DeepDive, ConfidenceRouter.Route(Rca(0.9, "logs vs db"), 1, 5, Options).Kind);

    [Fact]
    public void Low_confidence_deep_dives_until_max_rounds_then_escalates()
    {
        Assert.Equal(RouteKind.DeepDive, ConfidenceRouter.Route(Rca(0.6), 2, 3, Options).Kind);
        Assert.Equal(RouteKind.Escalate, ConfidenceRouter.Route(Rca(0.6), 3, 3, Options).Kind);
    }

    [Fact]
    public void No_new_evidence_in_a_deep_dive_round_escalates() =>
        Assert.Equal(RouteKind.Escalate, ConfidenceRouter.Route(Rca(0.6), 2, 0, Options).Kind);

    [Fact]
    public void Aggregator_deduplicates_evidence_and_detects_conflicts()
    {
        var state = new InvestigationState { IncidentId = "INC-1" };
        var logs = new AgentEvidence(AgentNames.Logs,
            [new Evidence("log", "SQL timeouts: pool exhausted", "get_recent_errors")],
            [new Hypothesis("Database connection pool exhaustion", 0.9)], []);
        var db = new AgentEvidence(AgentNames.Database,
            [new Evidence("database", "orders-db healthy now", "get_database_health"), new Evidence("log", "SQL timeouts:  pool exhausted", "dup")],
            [new Hypothesis("orders-db is healthy; database not the cause", 0.8)], ["pool history?"]);

        var result = EvidenceAggregator.Merge(state, [logs, db]);

        Assert.Equal(2, result.NewEvidence); // whitespace-normalized duplicate dropped
        Assert.Single(result.Conflicts);
        Assert.Contains("database", result.Conflicts[0]);
        Assert.Contains("pool history?", state.OpenQuestions);
    }

    [Fact]
    public void A_later_run_of_the_same_agent_supersedes_its_earlier_hypotheses()
    {
        var state = new InvestigationState { IncidentId = "INC-1" };
        EvidenceAggregator.Merge(state,
        [
            new AgentEvidence(AgentNames.Logs, [], [new Hypothesis("Database connection pool exhaustion", 0.9)], []),
            new AgentEvidence(AgentNames.Database, [], [new Hypothesis("database not the cause", 0.8)], []),
        ]);
        Assert.NotEmpty(state.Conflicts);

        EvidenceAggregator.Merge(state,
        [
            new AgentEvidence(AgentNames.Database, [new Evidence("connection-pool", "saturated 100/100", "get_connection_pool")], [new Hypothesis("Connection leak exhausted the database pool", 0.9)], []),
        ]);

        Assert.Empty(state.Conflicts);
    }

    [Fact]
    public void Thread_pool_is_not_mistaken_for_a_database_topic()
    {
        var conflicts = ConflictDetector.Detect(
        [
            new AgentEvidence(AgentNames.Logs, [], [new Hypothesis("CPU-bound processing is starving the thread pool", 0.7)], []),
            new AgentEvidence(AgentNames.Database, [], [new Hypothesis("search-db healthy; database not the cause", 0.8)], []),
        ]);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void Describing_the_failure_with_not_is_not_mistaken_for_ruling_the_topic_out()
    {
        var conflicts = ConflictDetector.Detect(
        [
            new AgentEvidence(AgentNames.Logs, [], [new Hypothesis("Database connection pool exhaustion causing SQL timeouts", 0.75)], []),
            new AgentEvidence(AgentNames.Database, [], [new Hypothesis("Connection leak: connections are not returned to the pool, exhausting the orders-db pool", 0.88)], []),
        ]);

        Assert.Empty(conflicts);
    }

    [Theory]
    [InlineData("orders-db connection pool was normal during the incident: database not the cause")]
    [InlineData("orders-db is healthy at the moment")]
    [InlineData("Database ruled out")]
    public void Ruling_the_topic_out_is_still_detected_as_a_conflict(string negative)
    {
        var conflicts = ConflictDetector.Detect(
        [
            new AgentEvidence(AgentNames.Logs, [], [new Hypothesis("Database connection pool exhaustion causing SQL timeouts", 0.75)], []),
            new AgentEvidence(AgentNames.Database, [], [new Hypothesis(negative, 0.75)], []),
        ]);

        Assert.Single(conflicts);
    }

    [Theory]
    [InlineData(AgentNames.Deployment, AgentNames.Database, true)]
    [InlineData(AgentNames.Logs, AgentNames.Deployment, true)]
    [InlineData(AgentNames.Database, AgentNames.Deployment, false)]
    [InlineData(AgentNames.Metrics, AgentNames.Deployment, false)]
    [InlineData(AgentNames.Database, AgentNames.RootCause, false)]
    public void Handoff_topology_is_explicit(string from, string to, bool allowed) =>
        Assert.Equal(allowed, HandoffTopology.IsAllowed(from, to));

    [Fact]
    public void Supervisor_fallback_maps_missing_evidence_to_specialists()
    {
        var agents = SupervisorFallback.FromMissingEvidence(["Release diff v1.41 → v1.42", "Connection pool history of orders-db"]);
        Assert.Equal(new[] { AgentNames.Deployment, AgentNames.Database }, agents);
    }
}
