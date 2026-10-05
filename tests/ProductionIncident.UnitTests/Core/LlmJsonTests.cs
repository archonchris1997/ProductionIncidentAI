using ProductionIncident.Core.Contracts;
using ProductionIncident.Core.Json;

namespace ProductionIncident.UnitTests.Core;

public sealed class LlmJsonTests
{
    [Fact]
    public void Parses_json_wrapped_in_markdown_fence_and_prose()
    {
        const string text = """
            Here is my analysis:
            ```json
            { "cause": "pool exhaustion", "confidence": 0.7, "supportingEvidence": ["a {b}"], "contradictions": [], "missingEvidence": [] }
            ```
            """;

        Assert.True(LlmJson.TryParse<RootCauseAnalysis>(text, out var rca, out var error), error);
        Assert.Equal("pool exhaustion", rca.Cause);
        Assert.Equal(0.7, rca.Confidence);
        Assert.Equal("a {b}", rca.SupportingEvidence[0]);
    }

    [Fact]
    public void Accepts_confidence_as_string()
    {
        Assert.True(LlmJson.TryParse<Hypothesis>("""{"cause":"x","confidence":"0.9"}""", out var h, out _));
        Assert.Equal(0.9, h.Confidence);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no json here")]
    [InlineData("{ \"cause\": ")]
    public void Rejects_invalid_output(string text)
    {
        Assert.False(LlmJson.TryParse<RootCauseAnalysis>(text, out _, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("DBAgent", AgentNames.Database)]
    [InlineData("logs", AgentNames.Logs)]
    [InlineData("Deployment_Agent", AgentNames.Deployment)]
    [InlineData("MetricsAgent", AgentNames.Metrics)]
    [InlineData("SecurityAgent", null)]
    public void Normalizes_specialist_names(string input, string? expected) =>
        Assert.Equal(expected, AgentNames.NormalizeSpecialist(input));
}
