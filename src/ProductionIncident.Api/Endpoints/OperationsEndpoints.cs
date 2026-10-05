using ProductionIncident.Core.Abstractions;
using ProductionIncident.Core.Tools;
using ProductionIncident.Skills;

namespace ProductionIncident.Api.Endpoints;

/// <summary>Read-only views over the system's building blocks: tools, skills, knowledge and memory.</summary>
public static class OperationsEndpoints
{
    public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/tools", async (IToolCatalog catalog, CancellationToken ct) =>
                Results.Ok((await catalog.GetToolsAsync(ct)).Select(t => new { t.Name, t.Domain, Risk = t.Risk.ToString(), t.Description })))
            .WithTags("System")
            .WithSummary("Tools available to agents and their risk classification.");

        app.MapGet("/skills", (ISkillCatalog skills) =>
                Results.Ok(skills.All.Select(s => new { s.Name, s.Version, s.Description })))
            .WithTags("System")
            .WithSummary("Versioned skills (procedural memory).");

        app.MapGet("/knowledge/search", async (string q, string? kind, IKnowledgeBase kb, CancellationToken ct) =>
                Results.Ok(await kb.SearchAsync(q, 5, kind, ct)))
            .WithTags("Knowledge")
            .WithSummary("RAG search over runbooks, architecture docs, postmortems and known errors.");

        app.MapGet("/memory/episodes", async (ILongTermMemory memory, CancellationToken ct) => Results.Ok(await memory.ListEpisodesAsync(ct)))
            .WithTags("Memory")
            .WithSummary("Episodic memory: past incidents and their resolutions.");

        app.MapGet("/memory/facts", async (ILongTermMemory memory, CancellationToken ct) => Results.Ok(await memory.ListFactsAsync(ct)))
            .WithTags("Memory")
            .WithSummary("Semantic memory: stable learned facts.");

        return app;
    }
}
