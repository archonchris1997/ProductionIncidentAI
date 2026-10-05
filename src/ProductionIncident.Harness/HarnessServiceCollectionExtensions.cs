using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProductionIncident.Core.Abstractions;
using ProductionIncident.Harness.Approvals;
using ProductionIncident.Harness.Budgets;
using ProductionIncident.Harness.Context;
using ProductionIncident.Harness.Control;
using ProductionIncident.Harness.Resilience;
using ProductionIncident.Harness.Workspace;

namespace ProductionIncident.Harness;

public static class HarnessServiceCollectionExtensions
{
    public static IServiceCollection AddIncidentHarness(this IServiceCollection services)
    {
        services.AddOptions<HarnessOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ApprovalPolicy>();
        services.TryAddSingleton<IApprovalService, InMemoryApprovalService>();
        services.TryAddSingleton<IInvestigationControl, InvestigationControl>();
        services.TryAddSingleton<ToolResultTrimmer>();
        services.TryAddSingleton<IWorkspace, InMemoryWorkspace>();
        services.TryAddSingleton<IBudgetTracker, BudgetTracker>();
        services.TryAddSingleton<RetryPolicy>();
        return services;
    }
}
