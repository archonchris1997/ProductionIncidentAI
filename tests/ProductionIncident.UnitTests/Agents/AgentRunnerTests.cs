using Microsoft.Extensions.AI;
using ProductionIncident.Agents.Runtime;
using ProductionIncident.Core.Contracts;
using ProductionIncident.LlmOps.Receipts;
using ProductionIncident.Tests.Shared;

namespace ProductionIncident.UnitTests.Agents;

public sealed class AgentRunnerTests
{
    private const string EvidenceJson = """{"agentName":"x","evidence":[{"type":"metric","description":"error rate 38%","source":"get_error_rate"}],"hypotheses":[],"openQuestions":[]}""";

    private static AgentDefinition Agent(params string[] tools) =>
        new("TestAgent", "test@1.0.0", "You are a test agent.", null, tools, "{}");

    private static AgentRunRequest Request(AgentDefinition agent, IReadOnlyList<string>? handoffTargets = null) => new()
    {
        IncidentId = "INC-T",
        Agent = agent,
        UserPrompt = PromptBuilder.BuildUserPrompt(AgentModes.DeepDive, null, "{}"),
        HandoffTargets = handoffTargets ?? [],
    };

    [Fact]
    public async Task Production_changing_tool_is_blocked_inside_the_agent_loop_even_if_exposed()
    {
        var chat = new QueueChatClient(
            (_, _) => QueueChatClient.ToolCall("rollback_deployment", new Dictionary<string, object?> { ["service"] = "checkout-api", ["targetVersion"] = "v1.41" }),
            (_, _) => QueueChatClient.Text(EvidenceJson));
        await using var system = TestSystem.Create(chatClient: chat);

        var run = await system.Get<AgentRunner>().RunAsync<AgentEvidence>(Request(Agent("rollback_deployment", "get_error_rate")), TestContext.Current.CancellationToken);

        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.Contains("rollback_deployment", run.BlockedToolCalls);
        Assert.Empty(system.Production.ExecutedOperations); // nothing touched production
        var toolMessage = chat.Calls[1].Messages.Last(m => m.Role == ChatRole.Tool);
        Assert.Contains("BLOCKED", toolMessage.Contents.OfType<FunctionResultContent>().Single().Result?.ToString());
    }

    [Fact]
    public async Task Tools_outside_the_allow_list_are_refused()
    {
        var chat = new QueueChatClient(
            (_, _) => QueueChatClient.ToolCall("get_connection_pool", new Dictionary<string, object?> { ["service"] = "checkout-api" }),
            (_, _) => QueueChatClient.Text(EvidenceJson));
        await using var system = TestSystem.Create(chatClient: chat);

        var run = await system.Get<AgentRunner>().RunAsync<AgentEvidence>(Request(Agent("get_error_rate")), TestContext.Current.CancellationToken);

        Assert.False(run.ToolCalls.Single().Success);
        Assert.Contains("not available", run.ToolCalls.Single().Result);
    }

    [Fact]
    public async Task Iteration_limit_forces_a_final_answer_without_tools()
    {
        var chat = new QueueChatClient((_, options) =>
            options?.Tools is { Count: > 0 }
                ? QueueChatClient.ToolCall("get_error_rate", new Dictionary<string, object?> { ["service"] = "checkout-api" })
                : QueueChatClient.Text(EvidenceJson));
        await using var system = TestSystem.Create(new Dictionary<string, string?> { ["Harness:MaxAgentIterations"] = "3" }, chat);

        var run = await system.Get<AgentRunner>().RunAsync<AgentEvidence>(Request(Agent("get_error_rate")), TestContext.Current.CancellationToken);

        Assert.Equal(AgentRunStatus.IterationLimit, run.Status);
        Assert.Equal(3, run.Iterations);
        Assert.NotNull(run.Output); // forced final answer still parsed
        Assert.Equal(4, chat.Calls.Count);
    }

    [Fact]
    public async Task Invalid_json_gets_one_repair_attempt()
    {
        var chat = new QueueChatClient(
            (_, _) => QueueChatClient.Text("I think it's the database."),
            (_, _) => QueueChatClient.Text(EvidenceJson));
        await using var system = TestSystem.Create(chatClient: chat);

        var run = await system.Get<AgentRunner>().RunAsync<AgentEvidence>(Request(Agent()), TestContext.Current.CancellationToken);

        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.Contains("not valid", chat.Calls[1].Messages.Last().Text);
    }

    [Fact]
    public async Task Handoff_to_a_target_outside_the_topology_is_rejected()
    {
        var chat = new QueueChatClient(
            (_, _) => QueueChatClient.ToolCall(HandoffTool.Name, new Dictionary<string, object?> { ["agent"] = "DeploymentAgent", ["reason"] = "look at the diff" }),
            (_, _) => QueueChatClient.Text(EvidenceJson));
        await using var system = TestSystem.Create(chatClient: chat);

        var run = await system.Get<AgentRunner>().RunAsync<AgentEvidence>(Request(Agent(), [AgentNames.Logs, AgentNames.Metrics]), TestContext.Current.CancellationToken);

        Assert.Null(run.Handoff);
        Assert.Contains("rejected", run.ToolCalls.Single().Result);
    }

    [Fact]
    public async Task Every_run_produces_a_turn_receipt()
    {
        var chat = new QueueChatClient(
            (_, _) => QueueChatClient.ToolCall("get_error_rate", new Dictionary<string, object?> { ["service"] = "checkout-api" }),
            (_, _) => QueueChatClient.Text(EvidenceJson));
        await using var system = TestSystem.Create(chatClient: chat);

        await system.Get<AgentRunner>().RunAsync<AgentEvidence>(Request(Agent("get_error_rate")), TestContext.Current.CancellationToken);

        var receipt = Assert.Single(system.Get<ITurnReceiptStore>().List("INC-T"));
        Assert.Equal("TestAgent", receipt.Agent);
        Assert.Equal("test@1.0.0", receipt.PromptVersion);
        Assert.Equal(new[] { "get_error_rate" }, receipt.ToolCalls);
        Assert.Equal(200, receipt.TokensIn);
        Assert.True(receipt.CostUsd > 0);
    }
}
