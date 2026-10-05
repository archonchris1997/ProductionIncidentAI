using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProductionIncident.LlmOps.Receipts;
using ProductionIncident.LlmOps.ReleaseGates;

namespace ProductionIncident.LlmOps;

public static class LlmOpsServiceCollectionExtensions
{
    public static IServiceCollection AddIncidentLlmOps(this IServiceCollection services)
    {
        services.AddOptions<LlmOpsOptions>();
        services.AddOptions<ReleaseGateThresholds>();
        services.TryAddSingleton<ITurnReceiptStore, InMemoryTurnReceiptStore>();
        services.TryAddSingleton<CostCalculator>();
        return services;
    }
}
