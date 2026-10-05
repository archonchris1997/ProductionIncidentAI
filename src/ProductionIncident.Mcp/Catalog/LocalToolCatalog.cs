using System.Reflection;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using ProductionIncident.Core.Json;
using ProductionIncident.Core.Tools;
using ProductionIncident.Mcp.Tools;

namespace ProductionIncident.Mcp.Catalog;

/// <summary>
/// In-process tool catalog (Milestone 1-5): the tool classes are invoked directly as AIFunctions,
/// no MCP transport involved. Same names/schemas as the MCP server exposes.
/// </summary>
public sealed class LocalToolCatalog : IToolCatalog
{
    private readonly IReadOnlyList<ToolDescriptor> _tools;

    public LocalToolCatalog(LogsTools logs, DatabaseTools database, MetricsTools metrics, DeploymentTools deployment, OperationsTools operations)
    {
        _tools = new object[] { logs, database, metrics, deployment, operations }
            .SelectMany(Describe)
            .ToList();
    }

    public ValueTask<IReadOnlyList<ToolDescriptor>> GetToolsAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_tools);

    private static IEnumerable<ToolDescriptor> Describe(object target)
    {
        foreach (var method in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            var attribute = method.GetCustomAttribute<McpServerToolAttribute>();
            if (attribute?.Name is not { } name)
            {
                continue;
            }

            var description = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description;
            var function = AIFunctionFactory.Create(method, target, name, description, JsonDefaults.Options);
            var (domain, risk) = ToolRegistry.Classify(name);
            yield return new ToolDescriptor(name, domain, risk, function);
        }
    }
}
