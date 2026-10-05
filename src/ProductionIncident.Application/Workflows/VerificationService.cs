using Microsoft.Extensions.AI;
using ProductionIncident.Core.Json;
using ProductionIncident.Core.Tools;
using ProductionIncident.Mcp.Models;

namespace ProductionIncident.Application.Workflows;

public sealed record VerificationResult(bool Recovered, string Summary);

/// <summary>Confirms recovery after mitigation with read-only tools (blueprint Phase H, step 37).</summary>
public sealed class VerificationService(IToolCatalog toolCatalog)
{
    public async Task<VerificationResult> VerifyAsync(string service, CancellationToken cancellationToken)
    {
        var tool = await toolCatalog.FindAsync("get_error_rate", cancellationToken).ConfigureAwait(false);
        if (tool is null || tool.Risk != ToolRisk.ReadOnly)
        {
            return new VerificationResult(false, "get_error_rate unavailable: recovery cannot be verified automatically.");
        }

        var raw = await tool.Function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["service"] = service, ["minutes"] = 15 }),
            cancellationToken).ConfigureAwait(false);
        var text = ToolResultFormatter.ToText(raw);

        if (!LlmJson.TryParse<MetricSeriesResult>(text, out var series, out var error))
        {
            return new VerificationResult(false, $"Could not read error rate: {error}");
        }

        var threshold = Math.Max(1.0, series.Baseline * 2);
        var recovered = series.Current <= threshold;
        return new VerificationResult(
            recovered,
            $"Error rate for {service}: {series.Current}% (baseline {series.Baseline}%, threshold {threshold}%) → {(recovered ? "recovered" : "NOT recovered")}. {series.Summary}");
    }
}
