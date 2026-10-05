using ProductionIncident.Core.Contracts;
using ProductionIncident.Core.Json;
using ProductionIncident.Core.Tools;
using ProductionIncident.Mcp.Tools;

namespace ProductionIncident.Application.Workflows;

public sealed record ValidatedRemediation(IReadOnlyList<ProposedAction> Actions, IReadOnlyList<string> Rejected);

/// <summary>
/// Deterministic checks on actions proposed by the RemediationAgent before they reach a human:
/// known production-changing tool, target limited to the affected services, risk normalized.
/// </summary>
public static class RemediationValidator
{
    private static readonly HashSet<string> AlwaysHighRisk = new(StringComparer.OrdinalIgnoreCase) { "execute_write_sql", "delete_resource" };
    private static readonly HashSet<string> Risks = new(StringComparer.OrdinalIgnoreCase) { "low", "medium", "high" };

    public static ValidatedRemediation Validate(RemediationPlan plan, IReadOnlyCollection<string> affectedServices)
    {
        var actions = new List<ProposedAction>();
        var rejected = new List<string>();

        foreach (var action in plan.ImmediateMitigation)
        {
            if (string.IsNullOrWhiteSpace(action.Tool) || !ToolRegistry.IsKnown(action.Tool))
            {
                rejected.Add($"'{action.Tool}': unknown tool.");
                continue;
            }

            if (ToolRegistry.Classify(action.Tool).Risk != ToolRisk.ProductionChanging)
            {
                rejected.Add($"'{action.Tool}': read-only tool, not a mitigation action.");
                continue;
            }

            var arguments = action.Arguments ?? new Dictionary<string, object?>();
            var service = arguments.TryGetValue("service", out var s) ? LlmJson.ArgToString(s) : null;
            if (service is not null && affectedServices.Count > 0 && !affectedServices.Contains(service, StringComparer.OrdinalIgnoreCase))
            {
                rejected.Add($"'{action.Tool}': target service '{service}' is not an affected service ({string.Join(", ", affectedServices)}).");
                continue;
            }

            var risk = AlwaysHighRisk.Contains(action.Tool)
                ? "high"
                : Risks.Contains(action.Risk ?? "") ? action.Risk!.ToLowerInvariant() : "medium";

            actions.Add(action with { Arguments = arguments, Risk = risk });
        }

        return new ValidatedRemediation(actions, rejected);
    }
}
