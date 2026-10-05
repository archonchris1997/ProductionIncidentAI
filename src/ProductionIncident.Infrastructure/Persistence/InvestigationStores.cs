using System.Collections.Concurrent;
using System.Text.Json;
using ProductionIncident.Core.Abstractions;
using ProductionIncident.Core.Json;
using ProductionIncident.Core.State;

namespace ProductionIncident.Infrastructure.Persistence;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    /// <summary>"InMemory" (default) or "File" (JSON snapshots on disk: survives restarts → checkpoint/resume).</summary>
    public string Provider { get; set; } = "InMemory";

    public string Directory { get; set; } = "data/investigations";
}

/// <summary>Snapshots are stored serialized, so callers never share the live object the workflow mutates.</summary>
public sealed class InMemoryInvestigationStore : IInvestigationStore
{
    private readonly ConcurrentDictionary<string, string> _snapshots = new();

    public Task SaveAsync(InvestigationState state, CancellationToken cancellationToken = default)
    {
        _snapshots[state.IncidentId] = JsonSerializer.Serialize(state, JsonDefaults.Options);
        return Task.CompletedTask;
    }

    public Task<InvestigationState?> GetAsync(string incidentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_snapshots.TryGetValue(incidentId, out var json) ? JsonSerializer.Deserialize<InvestigationState>(json, JsonDefaults.Options) : null);

    public Task<IReadOnlyList<InvestigationSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<InvestigationSummary> list = _snapshots.Values
            .Select(json => JsonSerializer.Deserialize<InvestigationState>(json, JsonDefaults.Options)!)
            .Select(InvestigationSummary.From)
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
        return Task.FromResult(list);
    }
}

/// <summary>
/// Durable checkpoints as JSON files (atomic replace). Good enough for a single instance;
/// use Cosmos DB / Azure SQL with optimistic concurrency for multi-instance deployments.
/// </summary>
public sealed class JsonFileInvestigationStore : IInvestigationStore
{
    private readonly string _directory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public JsonFileInvestigationStore(string directory)
    {
        _directory = Path.GetFullPath(directory);
        System.IO.Directory.CreateDirectory(_directory);
    }

    public async Task SaveAsync(InvestigationState state, CancellationToken cancellationToken = default)
    {
        var path = PathFor(state.IncidentId);
        var temp = path + ".tmp";
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(state, JsonDefaults.Indented), cancellationToken).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<InvestigationState?> GetAsync(string incidentId, CancellationToken cancellationToken = default)
    {
        var path = PathFor(incidentId);
        if (!File.Exists(path))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<InvestigationState>(json, JsonDefaults.Options);
    }

    public async Task<IReadOnlyList<InvestigationSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<InvestigationSummary>();
        foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "*.json"))
        {
            var json = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            if (JsonSerializer.Deserialize<InvestigationState>(json, JsonDefaults.Options) is { } state)
            {
                result.Add(InvestigationSummary.From(state));
            }
        }

        return result.OrderByDescending(s => s.CreatedAt).ToList();
    }

    private string PathFor(string incidentId)
    {
        var safe = string.Concat(incidentId.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_directory, safe + ".json");
    }
}
