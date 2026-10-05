using ProductionIncident.Core.State;

namespace ProductionIncident.Core.Abstractions;

/// <summary>
/// Session persistence + checkpoints. Every save is a full snapshot so an interrupted
/// workflow can resume from the last completed phase.
/// </summary>
public interface IInvestigationStore
{
    Task SaveAsync(InvestigationState state, CancellationToken cancellationToken = default);

    /// <summary>Returns a detached copy (never the live object the workflow is mutating).</summary>
    Task<InvestigationState?> GetAsync(string incidentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InvestigationSummary>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>Operator kill switch (blueprint §20 Safety).</summary>
public interface IInvestigationControl
{
    void RequestStop(string incidentId, string reason);

    bool IsStopRequested(string incidentId, out string? reason);

    void Clear(string incidentId);
}
