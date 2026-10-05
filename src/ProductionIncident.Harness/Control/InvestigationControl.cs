using System.Collections.Concurrent;
using ProductionIncident.Core.Abstractions;

namespace ProductionIncident.Harness.Control;

/// <summary>In-process kill switch. The workflow checks it before every phase and agent call.</summary>
public sealed class InvestigationControl : IInvestigationControl
{
    private readonly ConcurrentDictionary<string, string> _stops = new();

    public void RequestStop(string incidentId, string reason) => _stops[incidentId] = reason;

    public bool IsStopRequested(string incidentId, out string? reason) => _stops.TryGetValue(incidentId, out reason);

    public void Clear(string incidentId) => _stops.TryRemove(incidentId, out _);
}
