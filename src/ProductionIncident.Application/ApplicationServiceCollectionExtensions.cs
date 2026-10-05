using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProductionIncident.Agents;
using ProductionIncident.Application.Knowledge;
using ProductionIncident.Application.Workflows;
using ProductionIncident.Core.Abstractions;
using ProductionIncident.Harness;
using ProductionIncident.LlmOps;
using ProductionIncident.Memory.AdmissionGate;
using ProductionIncident.Memory.Consolidation;
using ProductionIncident.Memory.LongTerm;
using ProductionIncident.Rag.Indexing;
using ProductionIncident.Rag.Retrieval;

namespace ProductionIncident.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers agents, harness, LLMOps, RAG, memory and the workflow. Infrastructure provides
    /// IChatClient, IToolCatalog and IInvestigationStore.
    /// </summary>
    public static IServiceCollection AddIncidentApplication(this IServiceCollection services)
    {
        services.AddIncidentHarness();
        services.AddIncidentLlmOps();
        services.AddIncidentAgents();

        // RAG (seeded with the embedded knowledge corpus)
        services.TryAddSingleton<IKnowledgeBase>(_ =>
        {
            var kb = new InMemoryKnowledgeBase();
            EmbeddedKnowledgeSeeder.SeedAsync(kb).GetAwaiter().GetResult();
            return kb;
        });

        // Memory
        services.TryAddSingleton<ILongTermMemory>(_ => new InMemoryLongTermMemory());
        services.TryAddSingleton<MemoryAdmissionGate>();
        services.TryAddSingleton<MemoryConsolidator>();

        // Workflow
        services.TryAddSingleton<KnowledgeToolFactory>();
        services.TryAddSingleton<ActionExecutor>();
        services.TryAddSingleton<VerificationService>();
        services.TryAddSingleton<IInvestigationQueue, ChannelInvestigationQueue>();
        services.TryAddSingleton<IncidentWorkflow>();
        return services;
    }
}
