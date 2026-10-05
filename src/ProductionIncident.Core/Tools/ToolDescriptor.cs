using Microsoft.Extensions.AI;

namespace ProductionIncident.Core.Tools;

public enum ToolRisk
{
    /// <summary>Safe read operation: executed automatically.</summary>
    ReadOnly,

    /// <summary>Changes production: only executed after explicit human approval.</summary>
    ProductionChanging,
}

/// <summary>An executable capability (local C# method, MCP tool, ...) plus its safety classification.</summary>
public sealed record ToolDescriptor(string Name, string Domain, ToolRisk Risk, AIFunction Function)
{
    public string Description => Function.Description;
}

/// <summary>Source of tools for agents: in-process fakes/backends or remote MCP servers.</summary>
public interface IToolCatalog
{
    ValueTask<IReadOnlyList<ToolDescriptor>> GetToolsAsync(CancellationToken cancellationToken = default);
}

public static class ToolCatalogExtensions
{
    public static async ValueTask<ToolDescriptor?> FindAsync(this IToolCatalog catalog, string name, CancellationToken cancellationToken = default)
    {
        var tools = await catalog.GetToolsAsync(cancellationToken).ConfigureAwait(false);
        return tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public static async ValueTask<IReadOnlyList<ToolDescriptor>> GetByNamesAsync(this IToolCatalog catalog, IEnumerable<string> names, CancellationToken cancellationToken = default)
    {
        var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var tools = await catalog.GetToolsAsync(cancellationToken).ConfigureAwait(false);
        return tools.Where(t => wanted.Contains(t.Name)).ToList();
    }
}
