# Agents

| Agent | Skill | Tools (allow-list) | Output |
|---|---|---|---|
| TriageAgent | `triage-incident` | — | `TriageResult` |
| LogsAgent | `investigate-logs` | `get_recent_errors`, `search_logs`, `get_trace` | `AgentEvidence` |
| DatabaseAgent | `investigate-database` | `get_database_health`, `get_connection_pool`, `get_slow_queries`, `get_deadlocks` | `AgentEvidence` |
| MetricsAgent | `analyze-metrics` | `get_error_rate`, `get_latency`, `get_cpu`, `get_memory`, `get_request_rate` | `AgentEvidence` |
| DeploymentAgent | `deployment-analysis` | `get_recent_deployments`, `get_release_diff`, `get_config_changes`, `get_deployment_status` | `AgentEvidence` |
| RootCauseAgent | `root-cause-analysis` | `search_knowledge`, `recall_similar_incidents` (gated), read-only live checks | `RootCauseAnalysis` |
| SupervisorAgent | `supervise-investigation` | — | `SupervisorDecision` |
| RemediationAgent | `remediation-planning` | `get_deployment_status`, `get_recent_deployments`, `get_config_changes`, `search_knowledge` | `RemediationPlan` |

## Agent loop (AgentRunner)

1. System prompt = `# Agent` + `# Prompt-Version` + instructions + skill body + output contract + common rules.
2. User prompt = mode (`broad`, `deep-dive`, …), focus, and the **compacted** investigation context.
3. Loop (max `Harness:MaxAgentIterations`): call model → execute tool calls → append results → trim old results.
   - unknown / not-allowed tool → error result; production-changing tool → **BLOCKED** result (never executed);
   - `handoff_to` → validated against the topology, recorded once.
4. No tool call → parse JSON contract; one repair attempt on invalid JSON.
5. Iteration cap → one forced final call with tools disabled.
6. Receipt: model, prompt/skill versions, tool calls, blocked calls, handoff, tokens, cost, latency, memory ids.

Post-conditions are deterministic: the agent name is overwritten, confidences are clamped to [0, 1], Triage/Supervisor
agent names are normalized and filtered to known specialists.

## Handoff topology

```
Triage  → Logs, DB, Metrics, Deploy
Logs    → DB, Metrics, Deploy
DB      → Logs, Metrics
Metrics → Logs, DB
Deploy  → Logs, DB
```

Within a round, an agent already visited cannot be handed back to (no ping-pong) and `Harness:MaxHandoffsPerRound`
caps the chain.
