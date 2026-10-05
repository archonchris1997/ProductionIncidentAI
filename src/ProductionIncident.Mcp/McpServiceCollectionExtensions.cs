using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProductionIncident.Mcp.Backends;
using ProductionIncident.Mcp.Backends.Fake;
using ProductionIncident.Mcp.Tools;

namespace ProductionIncident.Mcp;

public static class McpServiceCollectionExtensions
{
    /// <summary>Registers the fake production backends (default until real integrations exist).</summary>
    public static IServiceCollection AddFakeProductionBackends(this IServiceCollection services)
    {
        services.TryAddSingleton(_ => new FakeProductionBackend());
        services.TryAddSingleton<ILogsBackend>(sp => sp.GetRequiredService<FakeProductionBackend>());
        services.TryAddSingleton<IDatabaseBackend>(sp => sp.GetRequiredService<FakeProductionBackend>());
        services.TryAddSingleton<IMetricsBackend>(sp => sp.GetRequiredService<FakeProductionBackend>());
        services.TryAddSingleton<IDeploymentBackend>(sp => sp.GetRequiredService<FakeProductionBackend>());
        services.TryAddSingleton<IOperationsBackend>(sp => sp.GetRequiredService<FakeProductionBackend>());
        return services;
    }

    /// <summary>Registers the tool classes (used both in-process and by the MCP server).</summary>
    public static IServiceCollection AddToolClasses(this IServiceCollection services)
    {
        services.TryAddSingleton<LogsTools>();
        services.TryAddSingleton<DatabaseTools>();
        services.TryAddSingleton<MetricsTools>();
        services.TryAddSingleton<DeploymentTools>();
        services.TryAddSingleton<OperationsTools>();
        return services;
    }
}
