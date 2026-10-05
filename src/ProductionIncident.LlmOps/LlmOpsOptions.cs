namespace ProductionIncident.LlmOps;

public sealed class LlmOpsOptions
{
    public const string SectionName = "LlmOps";

    /// <summary>USD per 1M input tokens for the configured model (used for receipts and budgets).</summary>
    public decimal InputPricePerMillionTokens { get; set; } = 2.50m;

    /// <summary>USD per 1M output tokens.</summary>
    public decimal OutputPricePerMillionTokens { get; set; } = 10.00m;
}
