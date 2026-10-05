using System.Collections.Concurrent;

namespace ProductionIncident.Harness.Workspace;

/// <summary>File-like workspace for full tool outputs referenced from trimmed context.</summary>
public interface IWorkspace
{
    string Put(string incidentId, string runId, string callId, string content);

    string? Get(string reference);
}

public sealed class InMemoryWorkspace : IWorkspace
{
    private readonly ConcurrentDictionary<string, string> _items = new();

    public string Put(string incidentId, string runId, string callId, string content)
    {
        var reference = $"ws://{incidentId}/{runId}/{callId}";
        _items[reference] = content;
        return reference;
    }

    public string? Get(string reference) => _items.GetValueOrDefault(reference);
}
