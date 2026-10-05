using System.ComponentModel;
using Microsoft.Extensions.AI;
using ProductionIncident.Core.Abstractions;
using ProductionIncident.Core.Json;
using ProductionIncident.Core.Tools;
using ProductionIncident.Mcp.Tools;

namespace ProductionIncident.Application.Knowledge;

public sealed record KnowledgeSearchHit(string SourceId, string Title, string Kind, string Excerpt, double Score);

public sealed record RecalledIncident(string IncidentId, string Service, string Summary, string RootCause, string Resolution, DateTimeOffset OccurredAt);

public sealed record RecallResult(IReadOnlyList<RecalledIncident> Incidents, IReadOnlyList<string> Facts);

/// <summary>
/// Exposes RAG (organizational knowledge) and long-term memory (past incidents) to agents as read-only tools.
/// MCP / tools = live state; RAG = documentation; memory = experience (blueprint §5).
/// </summary>
public sealed class KnowledgeToolFactory(IKnowledgeBase knowledgeBase, ILongTermMemory memory)
{
    public const string SearchKnowledgeName = "search_knowledge";
    public const string RecallIncidentsName = "recall_similar_incidents";

    public ToolDescriptor SearchKnowledge()
    {
        var tool = new SearchKnowledgeTool(knowledgeBase);
        var function = AIFunctionFactory.Create(
            (Func<string, string?, CancellationToken, Task<List<KnowledgeSearchHit>>>)tool.SearchAsync,
            SearchKnowledgeName,
            "Searches organizational knowledge (runbooks, architecture, postmortems, known errors). Returns excerpts with their source id for citation.",
            JsonDefaults.Options);

        return new ToolDescriptor(SearchKnowledgeName, ToolDomains.Knowledge, ToolRisk.ReadOnly, function);
    }

    /// <param name="retrievedSink">Receives the ids of recalled memories (for turn receipts and state).</param>
    public ToolDescriptor RecallSimilarIncidents(List<string> retrievedSink)
    {
        var tool = new RecallIncidentsTool(memory, retrievedSink);
        var function = AIFunctionFactory.Create(
            (Func<string, CancellationToken, Task<RecallResult>>)tool.RecallAsync,
            RecallIncidentsName,
            "Recalls similar past incidents (episodic memory) and stable facts (semantic memory). Context only: never proof of the current incident.",
            JsonDefaults.Options);

        return new ToolDescriptor(RecallIncidentsName, ToolDomains.Knowledge, ToolRisk.ReadOnly, function);
    }

    private sealed class SearchKnowledgeTool(IKnowledgeBase knowledgeBase)
    {
        public async Task<List<KnowledgeSearchHit>> SearchAsync(
            [Description("What to look for, e.g. 'pool exhaustion SQL timeout checkout-api'")] string query,
            [Description("Optional document kind: runbook, postmortem, architecture, known-error")] string? kind = null,
            CancellationToken cancellationToken = default)
        {
            var hits = await knowledgeBase.SearchAsync(query, 3, string.IsNullOrWhiteSpace(kind) ? null : kind, cancellationToken).ConfigureAwait(false);
            return hits.Select(h => new KnowledgeSearchHit(h.SourceId, h.Title, h.Kind, Excerpt(h.Content, 700), h.Score)).ToList();
        }
    }

    private sealed class RecallIncidentsTool(ILongTermMemory memory, List<string> sink)
    {
        public async Task<RecallResult> RecallAsync(
            [Description("Signals of the current incident, e.g. 'checkout-api sql timeout connection pool deployment'")] string query,
            CancellationToken cancellationToken = default)
        {
            var episodes = await memory.RecallEpisodesAsync(query, 3, cancellationToken).ConfigureAwait(false);
            var facts = await memory.RecallFactsAsync(query, 5, cancellationToken).ConfigureAwait(false);
            lock (sink)
            {
                sink.AddRange(episodes.Select(e => e.Id));
                sink.AddRange(facts.Select(f => f.Id));
            }

            return new RecallResult(
                episodes.Select(e => new RecalledIncident(e.IncidentId, e.Service, e.Summary, e.RootCause, e.Resolution, e.OccurredAt)).ToList(),
                facts.Select(f => f.ToString()).ToList());
        }
    }

    private static string Excerpt(string text, int max) => text.Length <= max ? text : text[..max] + "…";
}
