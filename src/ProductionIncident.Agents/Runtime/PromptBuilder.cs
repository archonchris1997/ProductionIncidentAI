using System.Text;
using ProductionIncident.Skills;

namespace ProductionIncident.Agents.Runtime;

/// <summary>
/// Builds versioned prompts. Layout (stable, parsed by evals and by the scripted model):
///   system: "# Agent: X" / "# Prompt-Version: x@1" / instructions / skill / output contract / rules
///   user:   "# Mode: ..." / "# Focus: ..." / &lt;incident-context&gt;{compact JSON}&lt;/incident-context&gt;
/// </summary>
public static class PromptBuilder
{
    public const string ContextOpen = "<incident-context>";
    public const string ContextClose = "</incident-context>";

    private const string CommonRules = """
        ## Rules
        - Evidence = facts returned by tools (cite the tool name as source). Hypotheses = your interpretation, with a confidence 0..1.
        - Never invent services, versions, numbers or timestamps. If you could not verify something, add it to the open questions.
        - Production-changing actions are never executed by you. If one is needed, describe it in your output.
        - Reply with ONLY the JSON object of the output contract (no markdown, no prose).
        """;

    public static string BuildSystemPrompt(AgentDefinition agent, SkillDefinition? skill)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Agent: {agent.Name}");
        sb.AppendLine($"# Prompt-Version: {agent.PromptVersion}");
        sb.AppendLine();
        sb.AppendLine(agent.Instructions.Trim());
        sb.AppendLine();

        if (skill is not null)
        {
            sb.AppendLine($"## Skill: {skill.VersionTag}");
            sb.AppendLine(skill.Body);
            sb.AppendLine();
        }

        sb.AppendLine("## Output contract");
        sb.AppendLine(agent.OutputContract.Trim());
        sb.AppendLine();
        sb.AppendLine(CommonRules);
        return sb.ToString();
    }

    public static string BuildUserPrompt(string mode, string? focus, string contextJson, string? extra = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Mode: {mode}");
        if (!string.IsNullOrWhiteSpace(focus))
        {
            sb.AppendLine($"# Focus: {focus}");
        }

        if (!string.IsNullOrWhiteSpace(extra))
        {
            sb.AppendLine();
            sb.AppendLine(extra.Trim());
        }

        sb.AppendLine();
        sb.AppendLine(ContextOpen);
        sb.AppendLine(contextJson);
        sb.AppendLine(ContextClose);
        return sb.ToString();
    }
}

public static class AgentModes
{
    public const string Triage = "triage";
    public const string Broad = "broad";
    public const string DeepDive = "deep-dive";
    public const string RootCause = "root-cause";
    public const string Supervise = "supervise";
    public const string Remediation = "remediation";
}
