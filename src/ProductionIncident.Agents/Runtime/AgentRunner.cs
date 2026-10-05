using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductionIncident.Core.Abstractions;
using ProductionIncident.Core.Contracts;
using ProductionIncident.Core.Json;
using ProductionIncident.Core.Tools;
using ProductionIncident.Harness;
using ProductionIncident.Harness.Approvals;
using ProductionIncident.Harness.Budgets;
using ProductionIncident.Harness.Context;
using ProductionIncident.Harness.Resilience;
using ProductionIncident.Harness.Workspace;
using ProductionIncident.LlmOps.Receipts;
using ProductionIncident.LlmOps.Tracing;
using ProductionIncident.Skills;

namespace ProductionIncident.Agents.Runtime;

/// <summary>
/// The bounded agent loop (blueprint §3.2): Reason → Act (tool call) → Observe → … → Final structured result.
/// The Harness owns the loop — not the model: iteration cap, timeouts/retries, budgets, approval gate,
/// tool-result trimming, handoff validation, kill switch, telemetry and turn receipts.
/// </summary>
public sealed class AgentRunner(
    IChatClient chatClient,
    IToolCatalog toolCatalog,
    ISkillCatalog skills,
    ApprovalPolicy approvalPolicy,
    ToolResultTrimmer trimmer,
    IWorkspace workspace,
    IBudgetTracker budget,
    RetryPolicy retry,
    ITurnReceiptStore receipts,
    CostCalculator costCalculator,
    IInvestigationControl control,
    IOptions<HarnessOptions> harnessOptions,
    IOptions<AgentModelOptions> modelOptions,
    TimeProvider timeProvider,
    ILogger<AgentRunner> logger)
{
    private const int MaxRepairAttempts = 1;
    private readonly HarnessOptions _options = harnessOptions.Value;

    public async Task<AgentRunResult<T>> RunAsync<T>(AgentRunRequest request, CancellationToken cancellationToken)
        where T : class
    {
        var agent = request.Agent;
        var runId = $"run-{Guid.NewGuid():N}"[..14];
        var startedAt = timeProvider.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();
        var skill = agent.SkillName is null ? null : skills.Get(agent.SkillName);

        using var activity = IncidentTelemetry.ActivitySource.StartActivity($"agent.run {agent.Name}");
        activity?.SetTag(IncidentTelemetry.Tags.IncidentId, request.IncidentId);
        activity?.SetTag(IncidentTelemetry.Tags.RunId, runId);
        activity?.SetTag(IncidentTelemetry.Tags.Agent, agent.Name);
        activity?.SetTag(IncidentTelemetry.Tags.Model, modelOptions.Value.Model);

        // Least privilege: the agent only sees the tools on its allow-list (+ per-run extras).
        var catalogTools = await toolCatalog.GetByNamesAsync(agent.ToolNames, cancellationToken).ConfigureAwait(false);
        var tools = catalogTools.Concat(request.ExtraTools)
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var aiTools = new List<AITool>(tools.Values.Select(t => t.Function));
        if (request.HandoffTargets.Count > 0)
        {
            aiTools.Add(HandoffTool.Create(request.HandoffTargets));
        }

        var chatOptions = new ChatOptions
        {
            Temperature = modelOptions.Value.Temperature,
            Tools = aiTools.Count > 0 ? aiTools : null,
            ToolMode = aiTools.Count > 0 ? ChatToolMode.Auto : null,
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, PromptBuilder.BuildSystemPrompt(agent, skill)),
            new(ChatRole.User, request.UserPrompt),
        };

        var toolCalls = new List<ToolCallRecord>();
        var blocked = new List<string>();
        var references = new Dictionary<string, string>();
        HandoffRequest? handoff = null;
        T? output = null;
        string? rawOutput = null;
        string? error = null;
        var status = AgentRunStatus.Completed;
        long tokensIn = 0, tokensOut = 0;
        var iterations = 0;
        var repairs = 0;
        var maxIterations = request.MaxIterations ?? _options.MaxAgentIterations;

        try
        {
            var finished = false;
            while (!finished && iterations < maxIterations)
            {
                iterations++;
                EnsureNotStopped(request.IncidentId);
                budget.EnsureWithinBudget(request.IncidentId);

                var response = await CallModelAsync(messages, chatOptions, agent.Name, cancellationToken).ConfigureAwait(false);
                (tokensIn, tokensOut) = Accumulate(request.IncidentId, response, tokensIn, tokensOut);
                messages.AddMessages(response);

                var calls = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();
                if (calls.Count == 0)
                {
                    rawOutput = response.Text;
                    if (LlmJson.TryParse<T>(rawOutput, out output, out error))
                    {
                        error = null;
                        finished = true;
                    }
                    else if (repairs < MaxRepairAttempts && iterations < maxIterations)
                    {
                        repairs++;
                        messages.Add(new ChatMessage(ChatRole.User,
                            $"Your reply is not valid for the output contract ({error}). Reply again with ONLY the JSON object, no prose."));
                    }
                    else
                    {
                        status = AgentRunStatus.InvalidOutput;
                        finished = true;
                    }

                    continue;
                }

                var results = new List<AIContent>(calls.Count);
                foreach (var call in calls)
                {
                    results.Add(await ExecuteCallAsync(call, request, runId, tools, toolCalls, blocked, references, h => handoff ??= h, handoff, cancellationToken).ConfigureAwait(false));
                }

                messages.Add(new ChatMessage(ChatRole.Tool, results));
                trimmer.TrimHistory(messages, references);
            }

            if (!finished)
            {
                // Iteration cap reached while the model still wanted tools: force a final answer without tools.
                status = AgentRunStatus.IterationLimit;
                messages.Add(new ChatMessage(ChatRole.User,
                    "Iteration limit reached. Do not call any tool. Reply now with ONLY the final JSON object based on the evidence you already have; list what you could not check in openQuestions/missingEvidence."));
                var finalOptions = chatOptions.Clone();
                finalOptions.Tools = null;
                finalOptions.ToolMode = ChatToolMode.None;
                var response = await CallModelAsync(messages, finalOptions, agent.Name, cancellationToken).ConfigureAwait(false);
                (tokensIn, tokensOut) = Accumulate(request.IncidentId, response, tokensIn, tokensOut);
                rawOutput = response.Text;
                if (LlmJson.TryParse<T>(rawOutput, out output, out error))
                {
                    error = $"Iteration limit ({maxIterations}) reached; final answer forced.";
                }
            }
        }
        catch (BudgetExceededException ex)
        {
            status = AgentRunStatus.BudgetExceeded;
            error = ex.Message;
        }
        catch (InvestigationStoppedException ex)
        {
            status = AgentRunStatus.Stopped;
            error = ex.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            status = AgentRunStatus.Failed;
            error = ex.Message;
            logger.LogError(ex, "{Agent} run {RunId} failed for {IncidentId}", agent.Name, runId, request.IncidentId);
        }

        stopwatch.Stop();
        var cost = costCalculator.Calculate(tokensIn, tokensOut);
        var latency = stopwatch.Elapsed.TotalMilliseconds;

        IncidentTelemetry.AgentLatencyMs.Record(latency, new KeyValuePair<string, object?>(IncidentTelemetry.Tags.Agent, agent.Name));
        activity?.SetTag(IncidentTelemetry.Tags.Status, status.ToString());
        if (status != AgentRunStatus.Completed)
        {
            activity?.SetStatus(ActivityStatusCode.Error, error);
        }

        receipts.Add(new TurnReceipt(
            RunId: runId,
            IncidentId: request.IncidentId,
            Agent: agent.Name,
            Model: modelOptions.Value.Model,
            PromptVersion: agent.PromptVersion,
            SkillVersion: skill?.VersionTag ?? "none",
            ToolCalls: toolCalls.Select(t => t.Name).ToList(),
            BlockedToolCalls: blocked,
            Handoff: handoff is null ? null : $"{agent.Name} → {handoff.Target}: {handoff.Reason}",
            Iterations: iterations,
            TokensIn: tokensIn,
            TokensOut: tokensOut,
            CostUsd: cost,
            LatencyMs: latency,
            MemoryRetrieved: request.RetrievedMemory.ToList(),
            MemoryStored: [],
            EvalScores: new Dictionary<string, double>(),
            FinalStatus: status.ToString(),
            StartedAt: startedAt));

        logger.LogInformation(
            "{Agent} {RunId} finished: {Status}, {Iterations} iteration(s), {ToolCalls} tool call(s), {Blocked} blocked, handoff={Handoff}",
            agent.Name, runId, status, iterations, toolCalls.Count, blocked.Count, handoff?.Target ?? "-");

        return new AgentRunResult<T>(runId, agent.Name, output, rawOutput, status, error, handoff, toolCalls, blocked, iterations, tokensIn, tokensOut, cost, latency);
    }

    private Task<ChatResponse> CallModelAsync(List<ChatMessage> messages, ChatOptions options, string agentName, CancellationToken cancellationToken) =>
        retry.ExecuteAsync(ct => chatClient.GetResponseAsync(messages, options, ct), _options.LlmCallTimeout, $"{agentName} model call", cancellationToken);

    private (long In, long Out) Accumulate(string incidentId, ChatResponse response, long tokensIn, long tokensOut)
    {
        long callIn = response.Usage?.InputTokenCount ?? 0;
        long callOut = response.Usage?.OutputTokenCount ?? 0;
        budget.Record(incidentId, callIn, callOut, costCalculator.Calculate(callIn, callOut));
        IncidentTelemetry.TokensIn.Add(callIn);
        IncidentTelemetry.TokensOut.Add(callOut);
        IncidentTelemetry.CostUsd.Add((double)costCalculator.Calculate(callIn, callOut));
        return (tokensIn + callIn, tokensOut + callOut);
    }

    private async Task<AIContent> ExecuteCallAsync(
        FunctionCallContent call,
        AgentRunRequest request,
        string runId,
        IReadOnlyDictionary<string, ToolDescriptor> tools,
        List<ToolCallRecord> toolCalls,
        List<string> blocked,
        Dictionary<string, string> references,
        Action<HandoffRequest> onHandoff,
        HandoffRequest? existingHandoff,
        CancellationToken cancellationToken)
    {
        var argsText = call.Arguments is null ? "{}" : JsonDefaults.Serialize(call.Arguments);
        IncidentTelemetry.ToolCalls.Add(1, new KeyValuePair<string, object?>(IncidentTelemetry.Tags.Tool, call.Name));

        // 1. Handoff (validated against the allowed topology — no arbitrary agent-to-agent routing).
        if (string.Equals(call.Name, HandoffTool.Name, StringComparison.OrdinalIgnoreCase))
        {
            var target = AgentNames.NormalizeSpecialist(LlmJson.ArgToString(GetArgument(call, "agent")));
            var reason = LlmJson.ArgToString(GetArgument(call, "reason")) ?? "";
            string message;
            if (existingHandoff is not null)
            {
                message = $"Handoff already requested to {existingHandoff.Target}. Reply with your final JSON now.";
            }
            else if (target is null || !request.HandoffTargets.Contains(target, StringComparer.OrdinalIgnoreCase))
            {
                message = $"Handoff rejected: allowed targets are [{string.Join(", ", request.HandoffTargets)}].";
            }
            else
            {
                onHandoff(new HandoffRequest(target, reason));
                IncidentTelemetry.Handoffs.Add(1);
                message = $"Handoff to {target} accepted. Reply now with your final JSON evidence; {target} will continue.";
            }

            toolCalls.Add(new ToolCallRecord(call.Name, argsText, message, true));
            return new FunctionResultContent(call.CallId, message);
        }

        // 2. Allow-list.
        if (!tools.TryGetValue(call.Name, out var tool))
        {
            var message = $"Error: tool '{call.Name}' is not available to {request.Agent.Name}.";
            toolCalls.Add(new ToolCallRecord(call.Name, argsText, message, false));
            return new FunctionResultContent(call.CallId, message);
        }

        // 3. Approval policy: production-changing tools never execute inside an agent loop.
        if (approvalPolicy.Decide(tool) == ToolDecision.RequireApproval)
        {
            blocked.Add(call.Name);
            IncidentTelemetry.BlockedActions.Add(1, new KeyValuePair<string, object?>(IncidentTelemetry.Tags.Tool, call.Name));
            var message = $"BLOCKED by harness: '{call.Name}' changes production and requires explicit human approval. Do not retry it; include it as a proposed action in your output instead.";
            toolCalls.Add(new ToolCallRecord(call.Name, argsText, message, false));
            return new FunctionResultContent(call.CallId, message);
        }

        // 4. Execute (timeout + retry), keep full result in the workspace, trim what goes back to the model.
        var sw = Stopwatch.StartNew();
        try
        {
            var arguments = new AIFunctionArguments(call.Arguments);
            var raw = await retry.ExecuteAsync(
                async ct => await tool.Function.InvokeAsync(arguments, ct).ConfigureAwait(false),
                _options.ToolCallTimeout,
                $"tool {call.Name}",
                cancellationToken).ConfigureAwait(false);

            var text = ToolResultFormatter.ToText(raw);
            var reference = workspace.Put(request.IncidentId, runId, call.CallId, text);
            references[call.CallId] = reference;
            toolCalls.Add(new ToolCallRecord(call.Name, argsText, text, !text.StartsWith("Error:", StringComparison.Ordinal)));
            return new FunctionResultContent(call.CallId, trimmer.TrimOnArrival(text, reference));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var message = $"Error: {call.Name} failed: {ex.Message}";
            toolCalls.Add(new ToolCallRecord(call.Name, argsText, message, false));
            return new FunctionResultContent(call.CallId, message);
        }
        finally
        {
            IncidentTelemetry.ToolLatencyMs.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>(IncidentTelemetry.Tags.Tool, call.Name));
        }
    }

    private static object? GetArgument(FunctionCallContent call, string name) =>
        call.Arguments is not null && call.Arguments.TryGetValue(name, out var value) ? value : null;

    private void EnsureNotStopped(string incidentId)
    {
        if (control.IsStopRequested(incidentId, out var reason))
        {
            throw new InvestigationStoppedException($"Investigation stopped by operator: {reason}");
        }
    }
}

public sealed class InvestigationStoppedException(string message) : Exception(message);
