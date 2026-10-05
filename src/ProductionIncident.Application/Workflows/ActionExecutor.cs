using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ProductionIncident.Core.Abstractions;
using ProductionIncident.Core.Json;
using ProductionIncident.Core.Tools;
using ProductionIncident.LlmOps.Tracing;

namespace ProductionIncident.Application.Workflows;

/// <summary>
/// The ONLY component allowed to invoke production-changing tools, and only for approved requests.
/// Deterministic: no model in this path.
/// </summary>
public sealed class ActionExecutor(IToolCatalog toolCatalog, IApprovalService approvals, ILogger<ActionExecutor> logger)
{
    public async Task<IReadOnlyList<string>> ExecuteApprovedAsync(string incidentId, CancellationToken cancellationToken)
    {
        var executed = new List<string>();
        var approved = await approvals.ListAsync(incidentId, ApprovalStatus.Approved, cancellationToken).ConfigureAwait(false);

        foreach (var request in approved)
        {
            using var activity = IncidentTelemetry.ActivitySource.StartActivity($"action.execute {request.Action.Tool}");
            activity?.SetTag(IncidentTelemetry.Tags.IncidentId, incidentId);
            activity?.SetTag(IncidentTelemetry.Tags.Tool, request.Action.Tool);
            activity?.SetTag("approval.id", request.Id);
            activity?.SetTag("approval.decided_by", request.DecidedBy);

            var tool = await toolCatalog.FindAsync(request.Action.Tool, cancellationToken).ConfigureAwait(false);
            if (tool is null)
            {
                await approvals.MarkExecutedAsync(request.Id, false, "Tool not available.", cancellationToken).ConfigureAwait(false);
                executed.Add($"{request.Action.Tool}: FAILED (tool not available)");
                continue;
            }

            try
            {
                var arguments = new AIFunctionArguments(request.Action.Arguments.ToDictionary(kv => kv.Key, kv => kv.Value));
                var raw = await tool.Function.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
                var text = ToolResultFormatter.ToText(raw);
                var success = !text.StartsWith("Error:", StringComparison.Ordinal);
                await approvals.MarkExecutedAsync(request.Id, success, text, cancellationToken).ConfigureAwait(false);
                executed.Add($"{request.Action.Tool}({FormatArgs(request.Action.Arguments)}) approved by {request.DecidedBy}: {(success ? "OK" : "FAILED")} — {text}");
                logger.LogWarning("Production action {Tool} executed for {IncidentId} (approval {ApprovalId} by {DecidedBy}): {Result}",
                    request.Action.Tool, incidentId, request.Id, request.DecidedBy, text);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await approvals.MarkExecutedAsync(request.Id, false, ex.Message, cancellationToken).ConfigureAwait(false);
                executed.Add($"{request.Action.Tool}: FAILED ({ex.Message})");
                logger.LogError(ex, "Production action {Tool} failed for {IncidentId}", request.Action.Tool, incidentId);
            }
        }

        return executed;
    }

    private static string FormatArgs(IReadOnlyDictionary<string, object?> arguments) =>
        string.Join(", ", arguments.Select(kv => $"{kv.Key}={LlmJson.ArgToString(kv.Value)}"));
}
