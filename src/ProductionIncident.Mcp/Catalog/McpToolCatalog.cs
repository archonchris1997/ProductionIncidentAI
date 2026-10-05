using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ProductionIncident.Core.Tools;
using ProductionIncident.Mcp.Tools;

namespace ProductionIncident.Mcp.Catalog;

/// <summary>
/// Remote tool catalog (Milestone 6): connects to the configured MCP servers over Streamable HTTP and exposes
/// their tools. <see cref="McpClientTool"/> already is an AIFunction, so agents use them unchanged.
/// Risk classification is done client-side by <see cref="ToolRegistry"/> (server annotations are only hints).
/// </summary>
public sealed class McpToolCatalog(IOptions<McpOptions> options, ILoggerFactory loggerFactory) : IToolCatalog, IAsyncDisposable
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly List<McpClient> _clients = [];
    private readonly ILogger _logger = loggerFactory.CreateLogger<McpToolCatalog>();
    private IReadOnlyList<ToolDescriptor>? _tools;

    public async ValueTask<IReadOnlyList<ToolDescriptor>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        if (_tools is not null)
        {
            return _tools;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_tools is not null)
            {
                return _tools;
            }

            var tools = new List<ToolDescriptor>();
            foreach (var server in options.Value.Servers)
            {
                var transportOptions = new HttpClientTransportOptions
                {
                    Name = server.Name,
                    Endpoint = new Uri(server.Endpoint),
                };

                if (!string.IsNullOrWhiteSpace(server.AuthHeaderName) && !string.IsNullOrWhiteSpace(server.AuthHeaderValue))
                {
                    transportOptions.AdditionalHeaders = new Dictionary<string, string> { [server.AuthHeaderName] = server.AuthHeaderValue };
                }

                var client = await McpClient.CreateAsync(new HttpClientTransport(transportOptions, loggerFactory), loggerFactory: loggerFactory, cancellationToken: cancellationToken).ConfigureAwait(false);
                _clients.Add(client);

                foreach (var tool in await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
                {
                    var (domain, risk) = ToolRegistry.Classify(tool.Name);
                    if (!ToolRegistry.IsKnown(tool.Name))
                    {
                        _logger.LogWarning("MCP server {Server} exposes unknown tool {Tool}; classified as production-changing", server.Name, tool.Name);
                    }

                    tools.Add(new ToolDescriptor(tool.Name, domain, risk, tool));
                }

                _logger.LogInformation("Connected to MCP server {Server} at {Endpoint}", server.Name, server.Endpoint);
            }

            _tools = tools;
            return _tools;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }

        _initLock.Dispose();
    }
}
