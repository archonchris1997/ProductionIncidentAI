namespace ProductionIncident.Mcp.Catalog;

public sealed class McpOptions
{
    public const string SectionName = "Mcp";

    /// <summary>"Local" = in-process tools (default). "Remote" = tools discovered from MCP servers over HTTP.</summary>
    public string Mode { get; set; } = "Local";

    public List<McpServerEndpoint> Servers { get; set; } = [];

    public bool IsRemote => string.Equals(Mode, "Remote", StringComparison.OrdinalIgnoreCase);
}

public sealed class McpServerEndpoint
{
    public string Name { get; set; } = "";

    public string Endpoint { get; set; } = "";

    /// <summary>
    /// Optional header carrying a credential for the MCP server (e.g. "X-Api-Key"). The value comes from
    /// configuration (Key Vault / user secrets) and never enters model context.
    /// </summary>
    public string? AuthHeaderName { get; set; }

    public string? AuthHeaderValue { get; set; }
}
