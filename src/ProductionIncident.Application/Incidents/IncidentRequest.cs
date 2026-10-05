using System.ComponentModel.DataAnnotations;

namespace ProductionIncident.Application.Incidents;

/// <summary>Incident ingestion contract (API, webhook, Service Bus message).</summary>
public sealed record IncidentRequest
{
    /// <summary>Optional external id (e.g. from Azure Monitor). Generated when absent.</summary>
    [StringLength(64)]
    public string? IncidentId { get; init; }

    [Required, StringLength(200, MinimumLength = 3)]
    public required string Title { get; init; }

    [StringLength(4000)]
    public string Description { get; init; } = "";

    [StringLength(63)]
    public string? Service { get; init; }

    [StringLength(10)]
    public string? Severity { get; init; }

    [StringLength(40)]
    public string Source { get; init; } = "api";
}

public sealed record ApprovalDecisionRequest(bool Approved, string DecidedBy, string? Comment);

public sealed record StopRequest(string Reason);
