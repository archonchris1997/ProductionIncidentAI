namespace ProductionIncident.Core.Abstractions;

/// <summary>A document of organizational knowledge (runbook, postmortem, architecture doc...).</summary>
public sealed record KnowledgeDocument(
    string Id,
    string Title,
    string Kind,
    string Content,
    IReadOnlyList<string> Tags);

public sealed record KnowledgeHit(
    string SourceId,
    string Title,
    string Kind,
    string Content,
    double Score);

/// <summary>RAG: organizational knowledge, not live production state (blueprint §5).</summary>
public interface IKnowledgeBase
{
    Task<IReadOnlyList<KnowledgeHit>> SearchAsync(string query, int top = 5, string? kind = null, CancellationToken cancellationToken = default);

    Task IndexAsync(KnowledgeDocument document, CancellationToken cancellationToken = default);
}
