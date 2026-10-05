using System.Collections.Concurrent;
using ProductionIncident.Core.Abstractions;
using ProductionIncident.Core.Text;

namespace ProductionIncident.Memory.LongTerm;

/// <summary>
/// Semantic + episodic long-term memory (dev/test implementation). Production: Cosmos DB / Azure AI Search
/// with embeddings; the interface stays the same.
/// </summary>
public sealed class InMemoryLongTermMemory : ILongTermMemory
{
    private readonly ConcurrentDictionary<string, EpisodicMemoryItem> _episodes = new();
    private readonly ConcurrentDictionary<string, SemanticFact> _facts = new();

    public InMemoryLongTermMemory(IEnumerable<EpisodicMemoryItem>? episodes = null, IEnumerable<SemanticFact>? facts = null)
    {
        foreach (var e in episodes ?? MemorySeed.Episodes)
        {
            _episodes[e.Id] = e;
        }

        foreach (var f in facts ?? MemorySeed.Facts)
        {
            _facts[f.Id] = f;
        }
    }

    public Task<IReadOnlyList<EpisodicMemoryItem>> RecallEpisodesAsync(string query, int top = 3, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EpisodicMemoryItem> result = _episodes.Values
            .Select(e => (Item: e, Score: TextSearch.Overlap(query, $"{e.Service} {e.Summary} {e.RootCause} {string.Join(' ', e.Signals)}")))
            .Where(x => x.Score >= 0.2)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Item.OccurredAt)
            .Take(Math.Clamp(top, 1, 10))
            .Select(x => x.Item)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<SemanticFact>> RecallFactsAsync(string query, int top = 5, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SemanticFact> result = _facts.Values
            .Select(f => (Fact: f, Score: TextSearch.Overlap(query, f.ToString())))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(Math.Clamp(top, 1, 20))
            .Select(x => x.Fact)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<EpisodicMemoryItem>> ListEpisodesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EpisodicMemoryItem>>(_episodes.Values.OrderByDescending(e => e.OccurredAt).ToList());

    public Task<IReadOnlyList<SemanticFact>> ListFactsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SemanticFact>>(_facts.Values.OrderBy(f => f.Subject).ToList());

    public Task StoreEpisodeAsync(EpisodicMemoryItem item, CancellationToken cancellationToken = default)
    {
        _episodes[item.Id] = item;
        return Task.CompletedTask;
    }

    public Task StoreFactAsync(SemanticFact fact, CancellationToken cancellationToken = default)
    {
        _facts[fact.Id] = fact;
        return Task.CompletedTask;
    }
}

public static class MemorySeed
{
    public static IReadOnlyList<EpisodicMemoryItem> Episodes { get; } =
    [
        new EpisodicMemoryItem(
            "ep-INC-087",
            "INC-087",
            "checkout-api",
            "checkout-api HTTP 500 and SQL timeouts 15 minutes after release v1.37; orders-db connection pool exhausted.",
            "v1.37 removed SqlConnection disposal in OrderRepository → connection leak → pool exhaustion",
            "Rollback to v1.36 + disposal fix ('await using') + pool-usage integration test",
            ["sql timeout", "connection pool exhaustion", "connection leak", "deployment before incident", "orders-db"],
            new DateTimeOffset(2026, 7, 2, 10, 15, 0, TimeSpan.Zero)),
        new EpisodicMemoryItem(
            "ep-INC-064",
            "INC-064",
            "search-api",
            "search-api timeouts during a marketing campaign; CPU at 99% on all replicas.",
            "Traffic surge (4x) saturated CPU; HPA max replicas too low",
            "Scaled to 16 replicas; raised HPA max",
            ["cpu saturation", "traffic surge", "request rate", "scaling"],
            new DateTimeOffset(2026, 5, 18, 19, 40, 0, TimeSpan.Zero)),
    ];

    public static IReadOnlyList<SemanticFact> Facts { get; } =
    [
        new SemanticFact("fact-1", "checkout-api", "depends on", "orders-db", "arch:service-catalog"),
        new SemanticFact("fact-2", "checkout-api", "depends on", "payments-api", "arch:service-catalog"),
        new SemanticFact("fact-3", "search-api", "depends on", "search-db", "arch:service-catalog"),
        new SemanticFact("fact-4", "payments-api", "depends on", "payment-gateway (external, p50 ~800 ms)", "arch:service-catalog"),
        new SemanticFact("fact-5", "orders-db", "has max pool size", "100 per checkout-api instance", "arch:service-catalog"),
    ];
}
