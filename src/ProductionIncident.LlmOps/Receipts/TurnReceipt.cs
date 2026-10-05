using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace ProductionIncident.LlmOps.Receipts;

/// <summary>One receipt per significant agent run (blueprint §14.4) — auditability and reproduction.</summary>
public sealed record TurnReceipt(
    string RunId,
    string IncidentId,
    string Agent,
    string Model,
    string PromptVersion,
    string SkillVersion,
    IReadOnlyList<string> ToolCalls,
    IReadOnlyList<string> BlockedToolCalls,
    string? Handoff,
    int Iterations,
    long TokensIn,
    long TokensOut,
    decimal CostUsd,
    double LatencyMs,
    IReadOnlyList<string> MemoryRetrieved,
    IReadOnlyList<string> MemoryStored,
    IReadOnlyDictionary<string, double> EvalScores,
    string FinalStatus,
    DateTimeOffset StartedAt);

public interface ITurnReceiptStore
{
    void Add(TurnReceipt receipt);

    IReadOnlyList<TurnReceipt> List(string? incidentId = null);
}

public sealed class InMemoryTurnReceiptStore : ITurnReceiptStore
{
    private readonly ConcurrentQueue<TurnReceipt> _receipts = new();

    public void Add(TurnReceipt receipt) => _receipts.Enqueue(receipt);

    public IReadOnlyList<TurnReceipt> List(string? incidentId = null) =>
        _receipts.Where(r => incidentId is null || r.IncidentId == incidentId).OrderBy(r => r.StartedAt).ToList();
}

public sealed class CostCalculator(IOptions<LlmOpsOptions> options)
{
    private readonly LlmOpsOptions _options = options.Value;

    public decimal Calculate(long tokensIn, long tokensOut) =>
        (tokensIn * _options.InputPricePerMillionTokens + tokensOut * _options.OutputPricePerMillionTokens) / 1_000_000m;
}
