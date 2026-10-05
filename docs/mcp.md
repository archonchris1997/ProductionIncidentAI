# Tools and MCP

One set of tool classes (`src/ProductionIncident.Mcp/Tools`) is exposed two ways:

- **Local** (`Mcp:Mode=Local`, default): `LocalToolCatalog` wraps the methods with `AIFunctionFactory`.
- **Remote** (`Mcp:Mode=Remote`): `ProductionIncident.McpServer` serves them over MCP Streamable HTTP
  (`AddMcpServer().WithHttpTransport().WithTools<T>()`, `MapMcp("/mcp")`); `McpToolCatalog` connects with
  `McpClient` + `HttpClientTransport` and hands the `McpClientTool`s (which are `AIFunction`s) to the agents.

| Server profile | Tools | Used by |
|---|---|---|
| `investigation` | logs, database, metrics, deployment (read-only) | agents |
| `operations` | `rollback_deployment`, `restart_service`, `scale_service`, `change_configuration`, `kill_pod`, `execute_write_sql`, `delete_resource` | `ActionExecutor`, after approval only |
| `all` | both (local development with the fake backends only) | — |

Safety rules:

- Risk is decided client-side by `ToolRegistry`; MCP annotations (`ReadOnly`, `Destructive`) are hints only.
- Unknown tools are production-changing (deny by default).
- Inputs are validated server-side (`ToolInput`): names, versions, config keys, window clamps.
- Results carry a short `summary` and minimized data; large results are trimmed by the Harness and stored in the
  workspace by reference.
- Credentials stay in the MCP server (managed identity / Key Vault) — never in model context.

Real backends to implement (same interfaces as `Backends/IBackends.cs`): Log Analytics / App Insights (KQL),
SQL DMVs / `pg_stat_activity`, Azure Monitor / Prometheus, GitHub / Azure DevOps / Kubernetes.
