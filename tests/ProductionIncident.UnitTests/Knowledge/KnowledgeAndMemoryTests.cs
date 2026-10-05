using ProductionIncident.Core.Abstractions;
using ProductionIncident.Core.Contracts;
using ProductionIncident.Core.State;
using ProductionIncident.Memory.AdmissionGate;
using ProductionIncident.Memory.LongTerm;
using ProductionIncident.Memory.RetrievalGate;
using ProductionIncident.Rag.Indexing;
using ProductionIncident.Rag.Retrieval;
using ProductionIncident.Skills;

namespace ProductionIncident.UnitTests.Knowledge;

public sealed class KnowledgeAndMemoryTests
{
    [Fact]
    public async Task Rag_returns_the_checkout_runbook_with_its_source_id()
    {
        var kb = new InMemoryKnowledgeBase();
        await EmbeddedKnowledgeSeeder.SeedAsync(kb, TestContext.Current.CancellationToken);

        var hits = await kb.SearchAsync("checkout-api pool exhaustion SQL timeout leaked connections", 3, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEmpty(hits);
        Assert.Equal("runbook:checkout-sql-timeouts", hits[0].SourceId);
        Assert.Equal(hits.Count, hits.Select(h => h.SourceId).Distinct().Count()); // one hit per document
    }

    [Fact]
    public async Task Rag_filters_by_kind()
    {
        var kb = new InMemoryKnowledgeBase();
        await EmbeddedKnowledgeSeeder.SeedAsync(kb, TestContext.Current.CancellationToken);

        var hits = await kb.SearchAsync("connection leak checkout", 5, "postmortem", TestContext.Current.CancellationToken);

        Assert.All(hits, h => Assert.Equal("postmortem", h.Kind));
        Assert.Contains(hits, h => h.SourceId == "postmortem:INC-087");
    }

    [Fact]
    public void All_skills_are_embedded_and_versioned()
    {
        var skills = new EmbeddedSkillCatalog();

        string[] expected =
        [
            "triage-incident", "investigate-logs", "investigate-database", "analyze-metrics",
            "deployment-analysis", "root-cause-analysis", "supervise-investigation", "remediation-planning",
        ];
        foreach (var name in expected)
        {
            var skill = skills.Get(name);
            Assert.NotNull(skill);
            Assert.Equal("1.0.0", skill.Version);
            Assert.False(string.IsNullOrWhiteSpace(skill.Body));
        }

        Assert.Contains("logging-guide.md", skills.Get("investigate-logs")!.References.Keys);
    }

    [Fact]
    public void Retrieval_gate_skips_memory_for_live_state_agents()
    {
        var state = new InvestigationState { IncidentId = "INC-1" };

        Assert.False(MemoryRetrievalGate.ShouldRetrieve(AgentNames.Logs, state).Retrieve);
        Assert.False(MemoryRetrievalGate.ShouldRetrieve(AgentNames.RootCause, state).Retrieve); // no hypotheses yet

        state.AgentResults.Add(new AgentEvidence(AgentNames.Logs, [], [new Hypothesis("pool exhaustion", 0.7)], []));
        Assert.True(MemoryRetrievalGate.ShouldRetrieve(AgentNames.RootCause, state).Retrieve);
    }

    [Fact]
    public async Task Admission_gate_only_admits_verified_high_confidence_new_knowledge()
    {
        var memory = new InMemoryLongTermMemory();
        var gate = new MemoryAdmissionGate(memory);
        var episode = new EpisodicMemoryItem("ep-INC-9", "INC-9", "checkout-api", "summary", "leak", "rollback", [], DateTimeOffset.UtcNow);
        var ct = TestContext.Current.CancellationToken;

        Assert.False((await gate.EvaluateEpisodeAsync(episode, 0.95, verified: false, ct)).Admit);
        Assert.False((await gate.EvaluateEpisodeAsync(episode, 0.60, verified: true, ct)).Admit);
        Assert.True((await gate.EvaluateEpisodeAsync(episode, 0.95, verified: true, ct)).Admit);

        // seeded fact "checkout-api depends on orders-db" → duplicate
        Assert.False((await gate.EvaluateFactAsync(new SemanticFact("f", "checkout-api", "depends on", "orders-db", "x"), ct)).Admit);
        Assert.True((await gate.EvaluateFactAsync(new SemanticFact("g", "billing-api", "depends on", "billing-db", "x"), ct)).Admit);
    }

    [Fact]
    public async Task Episodic_recall_finds_similar_past_incident()
    {
        var memory = new InMemoryLongTermMemory();

        var episodes = await memory.RecallEpisodesAsync("checkout-api sql timeout connection pool exhaustion deployment", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("INC-087", episodes[0].IncidentId);
    }
}
