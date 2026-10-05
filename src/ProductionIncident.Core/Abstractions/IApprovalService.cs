using ProductionIncident.Core.Contracts;

namespace ProductionIncident.Core.Abstractions;

public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected,
    Executed,
    ExecutionFailed,
}

public sealed record ApprovalRequest(
    string Id,
    string IncidentId,
    ProposedAction Action,
    ApprovalStatus Status,
    DateTimeOffset RequestedAt,
    string? DecidedBy = null,
    DateTimeOffset? DecidedAt = null,
    string? Comment = null,
    string? ExecutionResult = null);

/// <summary>Human-in-the-loop gate for every production-changing action.</summary>
public interface IApprovalService
{
    Task<ApprovalRequest> RequestAsync(string incidentId, ProposedAction action, CancellationToken cancellationToken = default);

    Task<ApprovalRequest?> GetAsync(string approvalId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApprovalRequest>> ListAsync(string? incidentId = null, ApprovalStatus? status = null, CancellationToken cancellationToken = default);

    Task<ApprovalRequest?> DecideAsync(string approvalId, bool approved, string decidedBy, string? comment, CancellationToken cancellationToken = default);

    Task<ApprovalRequest?> MarkExecutedAsync(string approvalId, bool success, string result, CancellationToken cancellationToken = default);
}
