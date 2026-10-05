using ProductionIncident.Application.Incidents;
using ProductionIncident.Application.Workflows;
using ProductionIncident.Core.Abstractions;

namespace ProductionIncident.Api.Endpoints;

public static class ApprovalEndpoints
{
    public static IEndpointRouteBuilder MapApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/approvals").WithTags("Approvals");

        group.MapGet("/", async (string? incidentId, ApprovalStatus? status, IApprovalService approvals, CancellationToken ct) =>
                Results.Ok(await approvals.ListAsync(incidentId, status, ct)))
            .WithSummary("Production-changing actions waiting for (or after) a human decision.");

        group.MapPost("/{id}/decision", async (string id, ApprovalDecisionRequest decision, IApprovalService approvals, IncidentWorkflow workflow, CancellationToken ct) =>
        {
            // Production: DecidedBy comes from the authenticated principal (Entra ID), not from the body,
            // and the endpoint requires an "incident-approver" role.
            if (string.IsNullOrWhiteSpace(decision.DecidedBy))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["decidedBy"] = ["Required."] });
            }

            var current = await approvals.GetAsync(id, ct);
            if (current is null)
            {
                return Results.NotFound();
            }

            if (current.Status != ApprovalStatus.Pending)
            {
                return Results.Conflict(new { error = $"Approval {id} is already {current.Status}." });
            }

            var updated = await approvals.DecideAsync(id, decision.Approved, decision.DecidedBy.Trim(), decision.Comment, ct);
            var state = await workflow.OnApprovalDecidedAsync(current.IncidentId, ct);
            return Results.Ok(new { approval = updated, incidentPhase = state?.Phase });
        })
        .WithSummary("Approve or reject a proposed production action. The workflow resumes once all are decided.");

        return app;
    }
}
