using System.Text.RegularExpressions;
using ProductionIncident.Core.Contracts;
using ProductionIncident.Core.State;

namespace ProductionIncident.Application.Orchestration;

public sealed record AggregationResult(int NewEvidence, IReadOnlyList<string> Conflicts);

/// <summary>
/// Deterministic evidence aggregation (blueprint Phase D): merge, deduplicate, collect open questions, detect conflicts.
/// No LLM: aggregation must be reproducible and cheap.
/// </summary>
public static partial class EvidenceAggregator
{
    [GeneratedRegex("\\s+")]
    private static partial Regex Whitespace();

    public static AggregationResult Merge(InvestigationState state, IEnumerable<AgentEvidence> results)
    {
        var known = new HashSet<string>(state.AllEvidence().Select(Key), StringComparer.OrdinalIgnoreCase);
        var newEvidence = 0;

        foreach (var result in results)
        {
            var fresh = result.Evidence.Where(e => !string.IsNullOrWhiteSpace(e.Description) && known.Add(Key(e))).ToList();
            newEvidence += fresh.Count;
            state.AgentResults.Add(result with { Evidence = fresh });
            state.CompletedAgents.Add(result.AgentName);
        }

        var latest = LatestPerAgent(state);

        // Open questions = the unresolved questions of each agent's most recent run.
        state.OpenQuestions.Clear();
        state.OpenQuestions.AddRange(latest.SelectMany(r => r.OpenQuestions).Distinct(StringComparer.OrdinalIgnoreCase));

        state.Conflicts.Clear();
        state.Conflicts.AddRange(ConflictDetector.Detect(latest));

        return new AggregationResult(newEvidence, state.Conflicts.ToList());
    }

    /// <summary>The most recent result of each agent (a later run supersedes its earlier hypotheses).</summary>
    public static IReadOnlyList<AgentEvidence> LatestPerAgent(InvestigationState state) =>
        state.AgentResults
            .Select((r, i) => (r, i))
            .GroupBy(x => x.r.AgentName)
            .Select(g => g.OrderBy(x => x.i).Last().r)
            .ToList();

    private static string Key(Evidence e) => $"{e.Type}|{Whitespace().Replace(e.Description.Trim().ToLowerInvariant(), " ")}";
}

/// <summary>
/// Detects contradictory hypotheses between agents on the same topic (one asserts, another rules out).
/// Conflicts are resolved by evidence (RootCauseAgent + Supervisor), never by majority vote.
/// </summary>
public static partial class ConflictDetector
{
    public const double MinConfidence = 0.5;

    private static readonly (string Topic, string[] Keywords)[] Topics =
    [
        ("database", ["database", " db", "-db", "sql", "connection"]),
        ("cpu", ["cpu"]),
        ("memory", ["memory", "oom"]),
        ("deployment", ["deploy", "release", "regression"]),
        ("configuration", ["config"]),
        ("network", ["network", "dns", "tls"]),
    ];

    // Only phrases that rule the topic out count as negations; a bare "not"/"no" ("connections are not returned")
    // usually describes the failure itself.
    [GeneratedRegex(
        "^\\s*no\\b" +
        "|\\bnot\\s+(?:the\\s+|a\\s+)?(?:root\\s+)?cause\\b" +
        "|\\bnot\\s+(?:a|the)\\s+(?:\\w+\\s+)?(?:regression|issue|problem|factor)\\b" +
        "|\\bno\\s+(?:sign|signs|evidence)\\s+of\\b" +
        "|\\b(?:is|was|are|were|remains?|remained|looks?|looked)\\s+normal\\b" +
        "|\\b(?:healthy|ruled\\s+out|unrelated|unlikely)\\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex Negation();

    public static IReadOnlyList<string> Detect(IReadOnlyList<AgentEvidence> latest)
    {
        var conflicts = new List<string>();
        foreach (var (topic, keywords) in Topics)
        {
            var related = latest
                .SelectMany(r => r.Hypotheses.Select(h => (Agent: r.AgentName, Hypothesis: h)))
                .Where(x => x.Hypothesis.Confidence >= MinConfidence && keywords.Any(k => (" " + x.Hypothesis.Cause.ToLowerInvariant()).Contains(k, StringComparison.Ordinal)))
                .ToList();

            var positives = related.Where(x => !Negation().IsMatch(x.Hypothesis.Cause)).ToList();
            var negatives = related.Where(x => Negation().IsMatch(x.Hypothesis.Cause)).ToList();

            foreach (var p in positives)
            {
                foreach (var n in negatives.Where(n => n.Agent != p.Agent))
                {
                    conflicts.Add($"{topic}: {p.Agent} says \"{p.Hypothesis.Cause}\" ({p.Hypothesis.Confidence:0.00}) but {n.Agent} says \"{n.Hypothesis.Cause}\" ({n.Hypothesis.Confidence:0.00})");
                }
            }
        }

        return conflicts.Distinct().ToList();
    }
}
