using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace ProductionIncident.Tests.Shared;

/// <summary>
/// Test double: returns scripted responses in order. A responder may inspect the conversation/options,
/// e.g. to answer differently when tools were removed (forced final answer).
/// </summary>
public sealed class QueueChatClient(params Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatMessage>[] responders) : IChatClient
{
    private int _index;

    public List<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = [];

    public static ChatMessage ToolCall(string name, IDictionary<string, object?>? args = null) =>
        new(ChatRole.Assistant, [new FunctionCallContent($"call_{Guid.NewGuid():N}", name, args ?? new Dictionary<string, object?>())]);

    public static ChatMessage Text(string text) => new(ChatRole.Assistant, text);

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var snapshot = messages.ToList();
        Calls.Add((snapshot, options));
        var responder = responders[Math.Min(_index++, responders.Length - 1)];
        var reply = responder(snapshot, options);
        return Task.FromResult(new ChatResponse(reply)
        {
            Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 20, TotalTokenCount = 120 },
        });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
