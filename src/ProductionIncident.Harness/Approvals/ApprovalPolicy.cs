using Microsoft.Extensions.Options;
using ProductionIncident.Core.Tools;

namespace ProductionIncident.Harness.Approvals;

public enum ToolDecision
{
    Allow,
    RequireApproval,
}

/// <summary>
/// Read-only tools → automatic. Production-changing or unknown tools → explicit human approval (blueprint §13.4).
/// Deterministic code, never the LLM, decides this.
/// </summary>
public sealed class ApprovalPolicy(IOptions<HarnessOptions> options)
{
    private readonly HashSet<string> _autoApproved = new(options.Value.AutoApprovedTools, StringComparer.OrdinalIgnoreCase);

    public ToolDecision Decide(ToolDescriptor? tool)
    {
        if (tool is null)
        {
            // Deny by default: an unknown tool is treated as dangerous.
            return ToolDecision.RequireApproval;
        }

        if (tool.Risk == ToolRisk.ReadOnly)
        {
            return ToolDecision.Allow;
        }

        return _autoApproved.Contains(tool.Name) ? ToolDecision.Allow : ToolDecision.RequireApproval;
    }
}
