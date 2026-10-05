namespace ProductionIncident.Core.Abstractions;

/// <summary>Episodic memory: a previous incident and how it ended.</summary>
public sealed record EpisodicMemoryItem(
    string Id,
    string IncidentId,
    string Service,
    string Summary,
    string RootCause,
    string Resolution,
    IReadOnlyList<string> Signals,
    DateTimeOffset OccurredAt);

/// <summary>Semantic memory: a stable learned fact ("checkout-api depends on orders-db").</summary>
public sealed record SemanticFact(
    string Id,
    string Subject,
    string Predicate,
    string Object,
    string Source)
{
    public override string ToString() => $"{Subject} {Predicate} {Object}";
}

/// <summary>
/// Long-term memory (blueprint §6.2). Procedural memory lives in Skills.
/// </summary>
public interface ILongTermMemory
{
    Task<IReadOnlyList<EpisodicMemoryItem>> RecallEpisodesAsync(string query, int top = 3, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SemanticFact>> RecallFactsAsync(string query, int top = 5, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EpisodicMemoryItem>> ListEpisodesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SemanticFact>> ListFactsAsync(CancellationToken cancellationToken = default);

    Task StoreEpisodeAsync(EpisodicMemoryItem item, CancellationToken cancellationToken = default);

    Task StoreFactAsync(SemanticFact fact, CancellationToken cancellationToken = default);
}
