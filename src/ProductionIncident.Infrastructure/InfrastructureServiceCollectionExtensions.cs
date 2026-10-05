using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using ProductionIncident.Agents.Runtime;
using ProductionIncident.Application;
using ProductionIncident.Core.Abstractions;
using ProductionIncident.Core.Tools;
using ProductionIncident.Harness;
using ProductionIncident.Infrastructure.AI;
using ProductionIncident.Infrastructure.Hosting;
using ProductionIncident.Infrastructure.Persistence;
using ProductionIncident.LlmOps;
using ProductionIncident.LlmOps.ReleaseGates;
using ProductionIncident.LlmOps.Tracing;
using ProductionIncident.Mcp;
using ProductionIncident.Mcp.Catalog;

namespace ProductionIncident.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Composition root for the incident system (used by the API, the tests and the evals).</summary>
    public static IServiceCollection AddProductionIncidentSystem(this IServiceCollection services, IConfiguration configuration, bool addWorker = true)
    {
        // Options
        services.Configure<HarnessOptions>(configuration.GetSection(HarnessOptions.SectionName));
        services.Configure<LlmOpsOptions>(configuration.GetSection(LlmOpsOptions.SectionName));
        services.Configure<ReleaseGateThresholds>(configuration.GetSection(ReleaseGateThresholds.SectionName));
        services.Configure<AgentModelOptions>(configuration.GetSection(AgentModelOptions.SectionName));
        services.Configure<OpenAIConnectionOptions>(configuration.GetSection(OpenAIConnectionOptions.SectionName));
        services.Configure<McpOptions>(configuration.GetSection(McpOptions.SectionName));
        services.Configure<PersistenceOptions>(configuration.GetSection(PersistenceOptions.SectionName));
        services.Configure<WorkerOptions>(configuration.GetSection(WorkerOptions.SectionName));

        // LLM
        services.TryAddSingleton<IChatClient>(sp => ChatClientFactory.Create(
            sp.GetRequiredService<IOptions<AgentModelOptions>>().Value,
            sp.GetRequiredService<IOptions<OpenAIConnectionOptions>>().Value,
            sp.GetRequiredService<ILoggerFactory>()));

        // Tools: in-process fakes (default) or remote MCP servers.
        services.AddFakeProductionBackends();
        services.AddToolClasses();
        services.TryAddSingleton<LocalToolCatalog>();
        services.TryAddSingleton<McpToolCatalog>();
        services.TryAddSingleton<IToolCatalog>(sp =>
            sp.GetRequiredService<IOptions<McpOptions>>().Value.IsRemote
                ? sp.GetRequiredService<McpToolCatalog>()
                : sp.GetRequiredService<LocalToolCatalog>());

        // Session persistence / checkpoints
        services.TryAddSingleton<IInvestigationStore>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<PersistenceOptions>>().Value;
            return string.Equals(options.Provider, "File", StringComparison.OrdinalIgnoreCase)
                ? new JsonFileInvestigationStore(options.Directory)
                : new InMemoryInvestigationStore();
        });

        services.AddIncidentApplication();

        if (addWorker)
        {
            services.AddHostedService<InvestigationWorker>();
        }

        return services;
    }

    /// <summary>OpenTelemetry traces + metrics for agents, tools, the workflow and the LLM client.</summary>
    public static IServiceCollection AddIncidentObservability(this IServiceCollection services, IConfiguration configuration)
    {
        var builder = services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(IncidentTelemetry.SourceName))
            .WithMetrics(metrics => metrics.AddMeter(IncidentTelemetry.SourceName));

        if (!string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.UseOtlpExporter();
        }

        return services;
    }
}
