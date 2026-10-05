namespace ProductionIncident.Harness;

/// <summary>Operational limits around the whole agent/workflow system (blueprint §11, §13).</summary>
public sealed class HarnessOptions
{
    public const string SectionName = "Harness";

    // Workflow / multi-agent loop
    public int MaxInvestigationRounds { get; set; } = 3;
    public int MaxHandoffsPerRound { get; set; } = 3;
    public double ConfidenceThreshold { get; set; } = 0.85;

    // Agent loop
    public int MaxAgentIterations { get; set; } = 8;
    public TimeSpan LlmCallTimeout { get; set; } = TimeSpan.FromSeconds(90);
    public TimeSpan ToolCallTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxRetries { get; set; } = 2;
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    // Context management
    public int MaxToolResultChars { get; set; } = 6_000;
    public int KeepRecentToolResults { get; set; } = 3;
    public int TrimmedPreviewChars { get; set; } = 500;
    public int MaxFactsInContext { get; set; } = 30;

    // Budgets (per incident)
    public long MaxTokensPerIncident { get; set; } = 500_000;
    public decimal MaxCostPerIncidentUsd { get; set; } = 5m;

    // Approvals: tools listed here skip human approval. Keep empty in production.
    public List<string> AutoApprovedTools { get; set; } = [];
}
