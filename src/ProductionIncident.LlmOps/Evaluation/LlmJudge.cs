using Microsoft.Extensions.AI;
using ProductionIncident.Core.Json;

namespace ProductionIncident.LlmOps.Evaluation;

public sealed record JudgeScore(double Groundedness, double Completeness, double Relevance, string Rationale)
{
    public double Average => (Groundedness + Completeness + Relevance) / 3.0;
}

/// <summary>LLM-as-Judge for qualities deterministic checks can't capture.</summary>
public interface ILlmJudge
{
    Task<JudgeScore> JudgeAsync(RegressionCase testCase, InvestigationOutcome outcome, CancellationToken cancellationToken = default);
}

public sealed class ChatClientLlmJudge(IChatClient chatClient) : ILlmJudge
{
    public const string PromptVersion = "judge@1.0.0";

    private const string SystemPrompt = """
        # Agent: Judge
        You evaluate an automated production-incident investigation.
        Score each dimension from 0.0 to 1.0:
        - groundedness: every claim in the root cause is supported by the listed evidence;
        - completeness: the root cause explains the symptoms and the remediation addresses it;
        - relevance: evidence and remediation focus on the incident, without noise.
        Reply with ONLY JSON: {"groundedness":0.0,"completeness":0.0,"relevance":0.0,"rationale":"..."}
        """;

    public async Task<JudgeScore> JudgeAsync(RegressionCase testCase, InvestigationOutcome outcome, CancellationToken cancellationToken = default)
    {
        var user = $"""
            Incident: {testCase.Input.Title}
            Description: {testCase.Input.Description}

            Root cause ({outcome.Confidence:0.00}): {outcome.RootCause}
            Supporting evidence:
            {string.Join("\n", outcome.SupportingEvidence.Select(e => "- " + e))}

            All evidence:
            {string.Join("\n", outcome.Evidence.Select(e => "- " + e))}

            Remediation: {outcome.RemediationText}
            """;

        var response = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.System, SystemPrompt), new ChatMessage(ChatRole.User, user)],
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return LlmJson.TryParse<JudgeScore>(response.Text, out var score, out var error)
            ? score
            : new JudgeScore(0, 0, 0, $"Judge output unparseable: {error}");
    }
}
