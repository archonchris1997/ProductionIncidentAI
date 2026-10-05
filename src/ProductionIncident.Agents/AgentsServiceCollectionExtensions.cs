using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProductionIncident.Agents.Database;
using ProductionIncident.Agents.Deployment;
using ProductionIncident.Agents.Logs;
using ProductionIncident.Agents.Metrics;
using ProductionIncident.Agents.Remediation;
using ProductionIncident.Agents.RootCause;
using ProductionIncident.Agents.Runtime;
using ProductionIncident.Agents.Specialists;
using ProductionIncident.Agents.Supervisor;
using ProductionIncident.Agents.Triage;
using ProductionIncident.Skills;

namespace ProductionIncident.Agents;

public static class AgentsServiceCollectionExtensions
{
    public static IServiceCollection AddIncidentAgents(this IServiceCollection services)
    {
        services.TryAddSingleton<ISkillCatalog, EmbeddedSkillCatalog>();
        services.TryAddSingleton<AgentRunner>();

        services.TryAddSingleton<TriageAgent>();
        services.TryAddSingleton<SupervisorAgent>();
        services.TryAddSingleton<RootCauseAgent>();
        services.TryAddSingleton<RemediationAgent>();

        services.AddSingleton<ISpecialistAgent, LogsAgent>();
        services.AddSingleton<ISpecialistAgent, DatabaseAgent>();
        services.AddSingleton<ISpecialistAgent, MetricsAgent>();
        services.AddSingleton<ISpecialistAgent, DeploymentAgent>();
        return services;
    }
}
