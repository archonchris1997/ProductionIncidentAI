using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace ProductionIncident.Agents.Runtime;

/// <summary>
/// Declaration of the handoff tool offered to specialists in deep-dive mode.
/// The runner intercepts calls to it: the delegate below never runs in practice.
/// </summary>
public static class HandoffTool
{
    public const string Name = "handoff_to";

    public static AIFunction Create(IReadOnlyList<string> allowedTargets) =>
        AIFunctionFactory.Create(
            ([Description("Target agent name")] string agent, [Description("Why the target agent should continue (the clue to follow)")] string reason) =>
                $"Handoff to {agent} requested.",
            Name,
            $"Transfer control of the investigation to another specialist to follow the strongest clue. Allowed targets: {string.Join(", ", allowedTargets)}. Call at most once, then reply with your final JSON.");
}
