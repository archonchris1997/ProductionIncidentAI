using System.Text.RegularExpressions;
using ProductionIncident.Core.Abstractions;
using ProductionIncident.Core.State;
using ProductionIncident.Memory.AdmissionGate;

namespace ProductionIncident.Memory.Consolidation;

public sealed record ConsolidationResult(IReadOnlyList<string> Stored, IReadOnlyList<string> Rejected);

/// <summary>
/// Memory consolidation (blueprint §6.5): after an incident closes, extract durable knowledge from the
/// timeline into episodic and semantic memory, through the admission gate.
/// </summary>
public sealed partial class MemoryConsolidator(ILongTermMemory memory, MemoryAdmissionGate gate)
{
    private static readonly string[] SignalVocabulary =
    [
        "sql timeout", "timeout expired", "connection pool", "pool exhaustion", "connection leak", "dispos",
        "deployment", "release", "cpu saturat", "traffic surge", "request rate", "configuration change",
        "httpclient.timeout", "thread pool starvation", "deadlock", "slow quer", "memory",
    ];

    [GeneratedRegex("\\b([a-z][a-z0-9]*(?:-[a-z0-9]+)*-db)\\b")]
    private static partial Regex DatabaseRegex();

    public async Task<ConsolidationResult> ConsolidateAsync(InvestigationState state, CancellationToken cancellationToken = default)
    {
        var stored = new List<string>();
        var rejected = new List<string>();
        var service = state.Service ?? state.Triage?.AffectedServices.FirstOrDefault() ?? "unknown";
        var evidenceText = string.Join("\n", state.AllEvidence().Select(e => e.Description)).ToLowerInvariant();

        // Episodic
        var signals = SignalVocabulary.Where(evidenceText.Contains).ToList();
        var resolution = state.ExecutedActions.Count > 0
            ? string.Join("; ", state.ExecutedActions)
            : string.Join("; ", state.Remediation?.PermanentFix ?? []);

        var episode = new EpisodicMemoryItem(
            $"ep-{state.IncidentId}",
            state.IncidentId,
            service,
            $"{state.Title}. {state.Description}".Trim(),
            state.CurrentRootCause ?? "",
            resolution,
            signals,
            state.CreatedAt);

        var episodeDecision = await gate.EvaluateEpisodeAsync(episode, state.RootCauseConfidence, state.Verified == true, cancellationToken).ConfigureAwait(false);
        if (episodeDecision.Admit)
        {
            await memory.StoreEpisodeAsync(episode, cancellationToken).ConfigureAwait(false);
            stored.Add($"episode {episode.Id}");
        }
        else
        {
            rejected.Add($"episode {episode.Id}: {episodeDecision.Reason}");
        }

        // Semantic: service → database dependencies observed in evidence.
        foreach (var database in DatabaseRegex().Matches(evidenceText).Select(m => m.Groups[1].Value).Distinct())
        {
            var fact = new SemanticFact($"fact-{service}-{database}", service, "depends on", database, state.IncidentId);
            var decision = await gate.EvaluateFactAsync(fact, cancellationToken).ConfigureAwait(false);
            if (decision.Admit)
            {
                await memory.StoreFactAsync(fact, cancellationToken).ConfigureAwait(false);
                stored.Add($"fact '{fact}'");
            }
            else
            {
                rejected.Add($"fact '{fact}': {decision.Reason}");
            }
        }

        return new ConsolidationResult(stored, rejected);
    }
}
