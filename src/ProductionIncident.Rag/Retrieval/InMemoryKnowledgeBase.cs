using System.Collections.Concurrent;
using ProductionIncident.Core.Abstractions;
using ProductionIncident.Core.Text;
using ProductionIncident.Rag.Indexing;

namespace ProductionIncident.Rag.Retrieval;

/// <summary>
/// Lexical (BM25-style) in-memory knowledge base. Development/test implementation of <see cref="IKnowledgeBase"/>;
/// the production implementation is Azure AI Search (hybrid vector + keyword, metadata filters on kind/service).
/// Citations are preserved: every hit carries its source id.
/// </summary>
public sealed class InMemoryKnowledgeBase : IKnowledgeBase
{
    private const double K1 = 1.2;
    private const double B = 0.75;

    private readonly ConcurrentDictionary<string, (KnowledgeChunk Chunk, IReadOnlyList<string> Terms)> _chunks = new();

    public Task IndexAsync(KnowledgeDocument document, CancellationToken cancellationToken = default)
    {
        foreach (var key in _chunks.Keys.Where(k => k.StartsWith(document.Id + "#", StringComparison.Ordinal)))
        {
            _chunks.TryRemove(key, out _);
        }

        foreach (var chunk in MarkdownChunker.Chunk(document))
        {
            var terms = TextSearch.Tokenize($"{chunk.Title} {chunk.Section} {string.Join(' ', chunk.Tags)} {chunk.Content}");
            _chunks[chunk.ChunkId] = (chunk, terms);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<KnowledgeHit>> SearchAsync(string query, int top = 5, string? kind = null, CancellationToken cancellationToken = default)
    {
        var queryTerms = TextSearch.Tokenize(query).Distinct().ToList();
        var candidates = _chunks.Values
            .Where(c => kind is null || string.Equals(c.Chunk.Kind, kind, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (queryTerms.Count == 0 || candidates.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<KnowledgeHit>>([]);
        }

        var avgLength = candidates.Average(c => c.Terms.Count);
        var n = candidates.Count;
        var df = queryTerms.ToDictionary(t => t, t => candidates.Count(c => c.Terms.Contains(t)));

        var hits = candidates
            .Select(c =>
            {
                var score = 0.0;
                foreach (var term in queryTerms)
                {
                    var tf = c.Terms.Count(x => x == term);
                    if (tf == 0)
                    {
                        continue;
                    }

                    var idf = Math.Log(1 + (n - df[term] + 0.5) / (df[term] + 0.5));
                    score += idf * (tf * (K1 + 1)) / (tf + K1 * (1 - B + B * c.Terms.Count / avgLength));
                }

                return (c.Chunk, Score: score);
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .GroupBy(x => x.Chunk.SourceId) // one hit per source document
            .Select(g => g.First())
            .Take(Math.Clamp(top, 1, 20))
            .Select(x => new KnowledgeHit(x.Chunk.SourceId, $"{x.Chunk.Title} — {x.Chunk.Section}", x.Chunk.Kind, x.Chunk.Content, Math.Round(x.Score, 3)))
            .ToList();

        return Task.FromResult<IReadOnlyList<KnowledgeHit>>(hits);
    }
}
