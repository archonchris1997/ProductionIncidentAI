# Production Incident AI — multi-agent incident investigation (.NET 10)

Implementation of the blueprint in [`docs/architecture.md`](docs/architecture.md): specialist agents investigate a
production incident concurrently, reconcile contradictions through a supervised handoff deep dive, produce a
calibrated root cause, propose remediation and **only change production after explicit human approval**.

It runs fully offline out of the box (scripted model + fake production estate), and switches to a real LLM and
real MCP servers by configuration.

```
Alert / API ─► Gateway ─► HARNESS-controlled WORKFLOW
                          Triage ─► Concurrent round (Logs ∥ DB ∥ Metrics ∥ Deploy) ─► Evidence aggregator
                          ─► RootCause (+ RAG + long-term memory + live checks) ─► Confidence router
                               ├─ low  ─► Supervisor ─► Handoff deep dive (Deploy → DB …) ─► RootCause ↺
                               └─ high ─► Remediation ─► Human approval ─► Action ─► Verification
                          ─► Close & learn (report, memory consolidation, postmortem → RAG, evals, receipts)
```

For an interactive version (clickable phases, a step-by-step replay of a real run, agent loop and project map),
open [`docs/flow.html`](docs/flow.html) in a browser.

## Quick start

```bash
dotnet test                                   # unit, workflow, agent, regression evals + release gate, API e2e
dotnet run --project src/ProductionIncident.Api   # http://localhost:5100
```

Then use [`src/ProductionIncident.Api/ProductionIncident.Api.http`](src/ProductionIncident.Api/ProductionIncident.Api.http):

1. `POST /incidents` with `checkout-api HTTP 500 after deployment`
2. `GET /incidents/INC-123` — watch phase, todos, evidence, conflicts, handoffs, timeline
3. `GET /approvals?status=Pending` — the proposed `rollback_deployment` to `v1.41`
4. `POST /approvals/{id}/decision` — approve → action → verification → `Resolved`
5. `GET /incidents/INC-123/report` and `/receipts`

Three reference scenarios ship with the fake estate: **checkout-api** (DB connection leak from release v1.42 —
needs a deep dive), **search-api** (CPU saturation from a traffic surge), **payments-api** (bad config change).

## Solution layout (blueprint §15)

| Project | Responsibility |
|---|---|
| `ProductionIncident.Core` | Structured agent contracts (§8), `InvestigationState` (working memory), tool/RAG/memory/approval abstractions |
| `ProductionIncident.Agents` | Bounded **agent loop** (`AgentRunner`), prompts, 8 agents, handoff topology |
| `ProductionIncident.Skills` | Versioned `SKILL.md` procedures (embedded) — *Skill = procedure, Tool = capability* |
| `ProductionIncident.Mcp` | Tool classes (Logs/DB/Metrics/Deploy/Operations), risk registry, local catalog, **MCP client catalog**, fake backends |
| `ProductionIncident.McpServer` | The same tools exposed over **MCP (Streamable HTTP)**; `investigation` and `operations` profiles run as separate servers |
| `ProductionIncident.Rag` | Knowledge base (BM25 in-memory; Azure AI Search in production), markdown chunking, seed corpus from `docs/knowledge` |
| `ProductionIncident.Memory` | Semantic + episodic memory, **retrieval gate**, **admission gate**, **consolidator** |
| `ProductionIncident.Harness` | Planning/todos, compaction, tool-result trimming, budgets, retries/timeouts, approvals, kill switch, workspace, checkpoints |
| `ProductionIncident.LlmOps` | OpenTelemetry tracing/metrics, **turn receipts**, deterministic evals, LLM-as-judge, **release gate** |
| `ProductionIncident.Application` | `IncidentWorkflow`, evidence aggregator + conflict detector, confidence router, remediation validator, action executor, verification |
| `ProductionIncident.Infrastructure` | Composition root, `IChatClient` (Scripted / OpenAI / Azure OpenAI), stores, background worker, OTel |
| `ProductionIncident.Api` | ASP.NET Core minimal API + Azure Monitor webhook |

Tests: `UnitTests`, `WorkflowTests`, `AgentEvals`, `RegressionEvals` (dataset in `evals/`), `IntegrationTests`.

## How the blueprint maps to code

| Blueprint | Where |
|---|---|
| Agent loop bounded by the Harness (§3.2) | `Agents/Runtime/AgentRunner.cs` — max iterations, forced final answer, JSON repair, timeouts/retries, budget, kill switch |
| Structured contracts (§8) | `Core/Contracts/AgentContracts.cs`, parsed tolerantly by `Core/Json/LlmJson.cs` |
| Concurrent orchestration (§9.1) | `IncidentWorkflow.ConcurrentInvestigationAsync` (`Task.WhenAll`) |
| Handoff orchestration + explicit topology (§9.2) | `HandoffTopology`, `HandoffTool`; validated in the runner and again in `IncidentWorkflow.DeepDiveAsync` |
| Evidence aggregation, dedup, conflicts (§12, Phase D) | `Application/Orchestration/EvidenceAggregator.cs` (deterministic; latest run per agent supersedes) |
| Confidence router + termination (§10, §11) | `ConfidenceRouter` — threshold 0.85, no unresolved contradiction, max rounds, no-new-evidence stop |
| RAG vs live state vs memory (§5) | `search_knowledge` (RAG), `recall_similar_incidents` (memory), MCP tools (live) — `Application/Knowledge` |
| Retrieval / admission gates, consolidation (§6.3–6.5) | `Memory/RetrievalGate`, `Memory/AdmissionGate`, `Memory/Consolidation` |
| Approval policy (§13.4) | `Harness/Approvals/ApprovalPolicy.cs` (deny-by-default) + `RemediationValidator` + `ActionExecutor` (only caller of write tools) |
| Context compaction / tool-result trimming (§13.2–13.3) | `Harness/Context/ContextCompactor.cs`, `ToolResultTrimmer.cs` + `IWorkspace` references |
| Checkpoints / resume | state saved after every phase; `JsonFileInvestigationStore`; `ResumeInterruptedAsync` on startup |
| Turn receipts (§14.4) | `LlmOps/Receipts` — one per agent run + one per workflow |
| Evals + release gate (§14) | `LlmOps/Evaluation`, `LlmOps/ReleaseGates`, `tests/ProductionIncident.RegressionEvals`, `evals/datasets/regression.json` |
| Security principles (§18) | least-privilege tool allow-lists per agent, client-side risk registry (server annotations untrusted), server-side input validation (`ToolInput`), separate read/write MCP servers, secrets only in config/Key Vault |

## Using a real model

```bash
cd src/ProductionIncident.Api
dotnet user-secrets set "AI:Provider" "AzureOpenAI"
dotnet user-secrets set "AI:Model" "<deployment-name>"
dotnet user-secrets set "AI:OpenAI:Endpoint" "https://<resource>.openai.azure.com/openai/v1/"
dotnet user-secrets set "AI:OpenAI:ApiKey" "<key>"
```

(`AI:Provider=OpenAI` with an empty endpoint targets api.openai.com.) Then run the regression evals with the same
settings to gate the model: `dotnet test tests/ProductionIncident.RegressionEvals`.

The scripted model (`Infrastructure/Simulation`) is a deterministic stand-in that speaks the real `IChatClient`
protocol (function calls, tool results, JSON answers, token usage). It exists so the whole pipeline is testable in CI;
it is not an AI.

## Using MCP servers instead of in-process tools

```bash
dotnet run --project src/ProductionIncident.McpServer --launch-profile all    # :5103, dev: read + write tools, shared fake state
dotnet run --project src/ProductionIncident.Api -- --Mcp:Mode=Remote
```

In real environments run two servers with separate identities — `investigation` (:5101, read-only) and
`operations` (:5102, write tools) — and list both under `Mcp:Servers`. Agents only ever receive the read-only
tools; the `ActionExecutor` is the only caller of the operations server, after approval.

Agents get `McpClientTool`s (they are `AIFunction`s), the risk of every tool is decided client-side by
`ToolRegistry`, and unknown remote tools are treated as production-changing.

## Notes and next steps

- The agent loop is built directly on `Microsoft.Extensions.AI` (`IChatClient`), the abstraction Microsoft Agent
  Framework builds on, so the Harness — not a framework — owns iterations, approvals and trimming. Swapping agent
  internals for Agent Framework agents only touches `AgentRunner`.
- Replace the fake backends (`Mcp/Backends`) with Log Analytics / App Insights, Azure Monitor / Prometheus,
  SQL DMVs, GitHub / Azure DevOps / Kubernetes (Milestone 6), keeping the same tool names and DTOs.
- Production stores: approvals, receipts, memory and investigations are in-memory (investigations also as JSON
  files). Use Cosmos DB / Azure SQL; knowledge base → Azure AI Search (hybrid + metadata filters).
- The approval endpoint takes `decidedBy` from the body for local use; behind Entra ID it must come from the
  authenticated principal with an approver role.
- Calibrate `Harness:ConfidenceThreshold` with the eval results before relying on it.
