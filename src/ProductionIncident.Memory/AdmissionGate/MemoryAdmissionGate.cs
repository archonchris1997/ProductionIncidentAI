using ProductionIncident.Core.Abstractions;
using ProductionIncident.Core.Text;

namespace ProductionIncident.Memory.AdmissionGate;

public sealed record AdmissionDecision(bool Admit, string Reason);

/// <summary>
/// Memory admission gate (blueprint §6.4): only durable, verified, non-duplicate knowledge is persisted.
/// </summary>
public sealed class MemoryAdmissionGate(ILongTermMemory memory)
{
    public const double MinConfidence = 0.85;
    public const double DuplicateSimilarity = 0.9;

    public async Task<AdmissionDecision> EvaluateEpisodeAsync(EpisodicMemoryItem candidate, double confidence, bool verified, CancellationToken cancellationToken = default)
    {
        if (!verified)
        {
            return new AdmissionDecision(false, "Recovery not verified: outcome is not trustworthy.");
        }

        if (confidence < MinConfidence)
        {
            return new AdmissionDecision(false, $"Root-cause confidence {confidence:0.00} below {MinConfidence:0.00}.");
        }

        if (string.IsNullOrWhiteSpace(candidate.RootCause) || string.IsNullOrWhiteSpace(candidate.Resolution))
        {
            return new AdmissionDecision(false, "Episode lacks a root cause or a resolution.");
        }

        var existing = await memory.ListEpisodesAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Any(e => e.IncidentId == candidate.IncidentId))
        {
            return new AdmissionDecision(false, "Incident already stored.");
        }

        return new AdmissionDecision(true, "Verified, high-confidence incident with a resolution.");
    }

    public async Task<AdmissionDecision> EvaluateFactAsync(SemanticFact candidate, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(candidate.Subject) || string.IsNullOrWhiteSpace(candidate.Object))
        {
            return new AdmissionDecision(false, "Incomplete fact.");
        }

        var existing = await memory.ListFactsAsync(cancellationToken).ConfigureAwait(false);
        var duplicate = existing.FirstOrDefault(f => TextSearch.Overlap(candidate.ToString(), f.ToString()) >= DuplicateSimilarity);
        return duplicate is null
            ? new AdmissionDecision(true, "New stable fact.")
            : new AdmissionDecision(false, $"Duplicate of existing fact '{duplicate}'.");
    }
}
