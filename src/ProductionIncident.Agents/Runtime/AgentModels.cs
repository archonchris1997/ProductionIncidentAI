using ProductionIncident.Core.Tools;

namespace ProductionIncident.Agents.Runtime;

/// <summary>Static definition of an agent: identity, versioned instructions, skill and tool allow-list.</summary>
public sealed record AgentDefinition(
    string Name,
    string PromptVersion,
    string Instructions,
    string? SkillName,
    IReadOnlyList<string> ToolNames,
    string OutputContract);

public sealed class AgentRunRequest
{
    public required string IncidentId { get; init; }

    public required AgentDefinition Agent { get; init; }

    public required string UserPrompt { get; init; }

    /// <summary>Per-run tools not in the shared catalog (e.g. RAG/memory tools bound to this incident).</summary>
    public IReadOnlyList<ToolDescriptor> ExtraTools { get; init; } = [];

    /// <summary>Agents this agent may hand off to (empty = handoff disabled). Enforced by the runner.</summary>
    public IReadOnlyList<string> HandoffTargets { get; init; } = [];

    /// <summary>Filled by memory tools during the run; copied into the turn receipt.</summary>
    public List<string> RetrievedMemory { get; init; } = [];

    public int? MaxIterations { get; init; }
}

public enum AgentRunStatus
{
    Completed,
    InvalidOutput,
    IterationLimit,
    BudgetExceeded,
    Stopped,
    Failed,
}

public sealed record ToolCallRecord(string Name, string Arguments, string Result, bool Success);

public sealed record HandoffRequest(string Target, string Reason);

public sealed record AgentRunResult<T>(
    string RunId,
    string Agent,
    T? Output,
    string? RawOutput,
    AgentRunStatus Status,
    string? Error,
    HandoffRequest? Handoff,
    IReadOnlyList<ToolCallRecord> ToolCalls,
    IReadOnlyList<string> BlockedToolCalls,
    int Iterations,
    long TokensIn,
    long TokensOut,
    decimal CostUsd,
    double LatencyMs)
    where T : class
{
    public bool Succeeded => Status == AgentRunStatus.Completed && Output is not null;
}

public sealed class AgentModelOptions
{
    public const string SectionName = "AI";

    /// <summary>"Scripted" (offline deterministic stand-in) or "AzureOpenAI" / "OpenAI".</summary>
    public string Provider { get; set; } = "Scripted";

    /// <summary>Model / deployment name, recorded in receipts for reproducibility.</summary>
    public string Model { get; set; } = "scripted-v1";

    public float Temperature { get; set; }
}
