using System.Collections.Concurrent;
using ProductionIncident.Core.Abstractions;
using ProductionIncident.Core.Contracts;

namespace ProductionIncident.Harness.Approvals;

public sealed class InMemoryApprovalService(TimeProvider timeProvider) : IApprovalService
{
    private readonly ConcurrentDictionary<string, ApprovalRequest> _requests = new();
    private readonly Lock _gate = new();

    public Task<ApprovalRequest> RequestAsync(string incidentId, ProposedAction action, CancellationToken cancellationToken = default)
    {
        var request = new ApprovalRequest(
            Id: $"apr-{Guid.NewGuid():N}"[..16],
            IncidentId: incidentId,
            Action: action,
            Status: ApprovalStatus.Pending,
            RequestedAt: timeProvider.GetUtcNow());

        _requests[request.Id] = request;
        return Task.FromResult(request);
    }

    public Task<ApprovalRequest?> GetAsync(string approvalId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_requests.GetValueOrDefault(approvalId));

    public Task<IReadOnlyList<ApprovalRequest>> ListAsync(string? incidentId = null, ApprovalStatus? status = null, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ApprovalRequest> result = _requests.Values
            .Where(r => incidentId is null || r.IncidentId == incidentId)
            .Where(r => status is null || r.Status == status)
            .OrderBy(r => r.RequestedAt)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<ApprovalRequest?> DecideAsync(string approvalId, bool approved, string decidedBy, string? comment, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_requests.TryGetValue(approvalId, out var current) || current.Status != ApprovalStatus.Pending)
            {
                return Task.FromResult<ApprovalRequest?>(current);
            }

            var updated = current with
            {
                Status = approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected,
                DecidedBy = decidedBy,
                DecidedAt = timeProvider.GetUtcNow(),
                Comment = comment,
            };
            _requests[approvalId] = updated;
            return Task.FromResult<ApprovalRequest?>(updated);
        }
    }

    public Task<ApprovalRequest?> MarkExecutedAsync(string approvalId, bool success, string result, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_requests.TryGetValue(approvalId, out var current))
            {
                return Task.FromResult<ApprovalRequest?>(null);
            }

            var updated = current with
            {
                Status = success ? ApprovalStatus.Executed : ApprovalStatus.ExecutionFailed,
                ExecutionResult = result,
            };
            _requests[approvalId] = updated;
            return Task.FromResult<ApprovalRequest?>(updated);
        }
    }
}
