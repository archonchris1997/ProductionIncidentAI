using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace ProductionIncident.Harness.Budgets;

public sealed record BudgetSnapshot(long TokensIn, long TokensOut, decimal CostUsd, int LlmCalls)
{
    public long TotalTokens => TokensIn + TokensOut;
}

public sealed class BudgetExceededException(string message) : Exception(message);

/// <summary>Per-incident token/cost budget shared by all agents (thread-safe: specialists run concurrently).</summary>
public interface IBudgetTracker
{
    void EnsureWithinBudget(string incidentId);

    void Record(string incidentId, long tokensIn, long tokensOut, decimal costUsd);

    BudgetSnapshot Get(string incidentId);
}

public sealed class BudgetTracker(IOptions<HarnessOptions> options) : IBudgetTracker
{
    private readonly ConcurrentDictionary<string, BudgetSnapshot> _budgets = new();
    private readonly HarnessOptions _options = options.Value;

    public void EnsureWithinBudget(string incidentId)
    {
        var current = Get(incidentId);
        if (current.TotalTokens >= _options.MaxTokensPerIncident)
        {
            throw new BudgetExceededException($"Token budget exhausted for {incidentId}: {current.TotalTokens}/{_options.MaxTokensPerIncident}.");
        }

        if (current.CostUsd >= _options.MaxCostPerIncidentUsd)
        {
            throw new BudgetExceededException($"Cost budget exhausted for {incidentId}: ${current.CostUsd:0.0000}/${_options.MaxCostPerIncidentUsd}.");
        }
    }

    public void Record(string incidentId, long tokensIn, long tokensOut, decimal costUsd) =>
        _budgets.AddOrUpdate(
            incidentId,
            _ => new BudgetSnapshot(tokensIn, tokensOut, costUsd, 1),
            (_, b) => new BudgetSnapshot(b.TokensIn + tokensIn, b.TokensOut + tokensOut, b.CostUsd + costUsd, b.LlmCalls + 1));

    public BudgetSnapshot Get(string incidentId) =>
        _budgets.GetValueOrDefault(incidentId) ?? new BudgetSnapshot(0, 0, 0m, 0);
}
