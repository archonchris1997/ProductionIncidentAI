using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;
using ProductionIncident.Core.Json;

namespace ProductionIncident.Infrastructure.Simulation;

/// <summary>
/// Offline, deterministic stand-in for the LLM (AI:Provider = "Scripted").
/// It speaks the real IChatClient protocol — function calls, tool results, structured JSON answers, usage —
/// so the whole pipeline (agent loops, handoffs, approvals, receipts, evals) runs locally and in CI without a model.
/// Swap for Azure OpenAI by configuration; nothing else changes.
/// </summary>
public sealed class ScriptedChatClient : IChatClient
{
    public const string ModelId = "scripted-v1";
    private int _callCounter;

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var history = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        var conversation = ScriptedConversation.Parse(history, options);
        var action = ScriptedAgents.Next(conversation);

        var contents = new List<AIContent>();
        if (conversation.NewObservations.Count > 0 && action is CallTool)
        {
            // Model "notes": keep what was learned so it survives tool-result trimming.
            var notes = new StringBuilder();
            foreach (var (tool, summary) in conversation.NewObservations)
            {
                notes.AppendLine($"Observation from {tool}: {summary}");
            }

            contents.Add(new TextContent(notes.ToString()));
        }

        switch (action)
        {
            case CallTool call:
                var callId = $"call_{Interlocked.Increment(ref _callCounter):D4}";
                contents.Add(new FunctionCallContent(callId, call.Name, call.Arguments));
                break;
            case FinalAnswer final:
                contents.Add(new TextContent(JsonDefaults.Serialize(final.Payload)));
                break;
        }

        var reply = new ChatMessage(ChatRole.Assistant, contents);
        var inputChars = history.Sum(m => m.Text?.Length ?? 0) + history.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Sum(r => r.Result?.ToString()?.Length ?? 0);
        int inputTokens = Math.Max(1, inputChars / 4);
        int outputTokens = Math.Max(1, (reply.Text?.Length ?? 0) / 4 + (action is CallTool ? 20 : 0));

        var response = new ChatResponse(reply)
        {
            ModelId = ModelId,
            FinishReason = action is CallTool ? ChatFinishReason.ToolCalls : ChatFinishReason.Stop,
            Usage = new UsageDetails
            {
                InputTokenCount = inputTokens,
                OutputTokenCount = outputTokens,
                TotalTokenCount = inputTokens + outputTokens,
            },
        };

        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
