using System.ComponentModel.DataAnnotations;
using ProductionIncident.Application.Incidents;
using ProductionIncident.Application.Workflows;
using ProductionIncident.Core.Abstractions;
using ProductionIncident.LlmOps.Receipts;

namespace ProductionIncident.Api.Endpoints;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/incidents").WithTags("Incidents");

        group.MapPost("/", async (IncidentRequest request, IncidentWorkflow workflow, CancellationToken ct) =>
        {
            var errors = Validate(request);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var state = await workflow.StartAsync(request, ct);
            return Results.Accepted($"/incidents/{state.IncidentId}", new { state.IncidentId, state.Phase, links = Links(state.IncidentId) });
        })
        .WithSummary("Report an incident and start an investigation.");

        group.MapGet("/", async (IInvestigationStore store, CancellationToken ct) => Results.Ok(await store.ListAsync(ct)))
            .WithSummary("List investigations.");

        group.MapGet("/{id}", async (string id, IInvestigationStore store, CancellationToken ct) =>
                await store.GetAsync(id, ct) is { } state ? Results.Ok(state) : Results.NotFound())
            .WithSummary("Full investigation state: evidence, hypotheses, root cause, todos, timeline.");

        group.MapGet("/{id}/report", async (string id, IInvestigationStore store, CancellationToken ct) =>
                await store.GetAsync(id, ct) is { } state
                    ? Results.Text(state.FinalReport ?? IncidentReportBuilder.Build(state), "text/markdown")
                    : Results.NotFound())
            .WithSummary("Incident report / postmortem draft (markdown).");

        group.MapGet("/{id}/receipts", (string id, ITurnReceiptStore receipts) => Results.Ok(receipts.List(id)))
            .WithSummary("Turn receipts: one per agent run (model, prompt/skill versions, tools, tokens, cost, latency).");

        group.MapPost("/{id}/stop", async (string id, StopRequest request, IncidentWorkflow workflow, CancellationToken ct) =>
                await workflow.StopAsync(id, request.Reason, ct) is { } state ? Results.Ok(new { state.IncidentId, state.Phase, state.StopReason }) : Results.NotFound())
            .WithSummary("Operator kill switch.");

        return app;
    }

    private static object Links(string id) => new
    {
        self = $"/incidents/{id}",
        report = $"/incidents/{id}/report",
        receipts = $"/incidents/{id}/receipts",
        approvals = $"/approvals?incidentId={id}",
    };

    internal static Dictionary<string, string[]> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results
            .SelectMany(r => r.MemberNames.DefaultIfEmpty("").Select(m => (Member: m, r.ErrorMessage)))
            .GroupBy(x => x.Member)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage ?? "Invalid value.").ToArray());
    }
}
