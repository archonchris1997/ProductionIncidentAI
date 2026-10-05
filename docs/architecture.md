# Production Incident Multi-Agent System

## Technical Architecture & Development Blueprint

**Target stack:** .NET / C#, Microsoft Agent Framework, MCP, Azure services  
**Primary use case:** autonomous/semi-autonomous investigation of production incidents, root-cause analysis and remediation proposal  
**Status:** architecture blueprint ready for implementation

---

# 1. Goal

Build a production-grade AI system capable of investigating incidents by combining:

- multiple specialist agents;
- agent loops and tool calling;
- Skills and reusable procedures;
- MCP-based access to production systems;
- RAG over internal documentation;
- short-term and long-term memory;
- concurrent and handoff orchestration;
- explicit workflows;
- a Harness for operational control;
- LLMOps, evaluation, tracing and release gates.

The system must **assist with diagnosis first** and only execute production-changing actions after explicit approval.

---

# 2. High-Level Architecture

```text
                               USER / ALERT / EVENT
                                      │
                                      ▼
                             API / EVENT GATEWAY
                    ASP.NET Core / Webhook / Service Bus
                                      │
                                      ▼
┌────────────────────────────────────────────────────────────────────┐
│                              HARNESS                               │
│                                                                    │
│ Planning • Todos • Sessions • Context Compaction                   │
│ Tool Result Trimming • Bounded Loops • Timeouts • Retries         │
│ Budgets • Approvals • Checkpoints • Workspace • Observability      │
│                                                                    │
│                              WORKFLOW                              │
│                                 │                                  │
│                                 ▼                                  │
│                            TriageAgent                              │
│                                 │                                  │
│                                 ▼                                  │
│                    Concurrent Investigation                        │
│                                 │                                  │
│               ┌─────────────────┼─────────────────┐                │
│               ▼                 ▼                 ▼                │
│          LogsAgent          DBAgent         MetricsAgent           │
│               │                 │                 │                │
│               └─────────────────┼─────────────────┘                │
│                                 │                                  │
│                         DeploymentAgent                            │
│                                 │                                  │
│                                 ▼                                  │
│                    Shared Investigation State                      │
│                                 │                                  │
│                                 ▼                                  │
│                       Evidence Aggregator                          │
│                                 │                                  │
│                                 ▼                                  │
│                         RootCauseAgent                             │
│                          /      |      \                            │
│                         /       |       \                           │
│                       RAG   Long Memory   MCP live checks          │
│                                 │                                  │
│                                 ▼                                  │
│                        Confidence Check                            │
│                         /             \                            │
│                       low             high                         │
│                        │                │                           │
│                        ▼                ▼                           │
│                Handoff Deep Dive   RemediationAgent                │
│                        │                │                           │
│                 Logs ↔ DB ↔ Metrics      ▼                         │
│                      ↔ Deploy      Human Approval                  │
│                        │                │                           │
│                        └──────┐         ▼                           │
│                               └──→    Action                        │
│                                                                    │
└────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
                          TOOLS / MCP SERVERS
                 Logs • DB • Metrics • Deploy • Kubernetes
                                      │
                                      ▼
                   PRODUCTION SYSTEMS / CLOUD SERVICES

──────────────────────────────────────────────────────────────────────

                                LLMOps
     Evaluation • Tracing • Monitoring • Regression Tests
     Prompt/Skill Versioning • Model Versioning • Cost • Latency
     LLM-as-Judge • Release Gate • Feedback Loops
```

---

# 3. Core Concepts

## 3.1 Agent

An Agent is the decision-making unit.

```text
Agent
├── LLM
├── Instructions
├── Skills
├── Tools
├── MCP Client
├── RAG access
├── Session/context
└── Agent Loop
```

**Rule:** an Agent reasons; deterministic code should handle simple rules and invariant checks.

---

## 3.2 Agent Loop

Each agent may perform multiple reasoning/action cycles.

```text
Reason
  ↓
Act / Tool Call
  ↓
Observe Tool Result
  ↓
Reason
  ↓
...
  ↓
Final Result
```

Examples:

- `LogsAgent` calls `GetRecentErrors()`;
- observes SQL timeout;
- calls `GetTrace()`;
- observes `OrderRepository` latency;
- calls `SearchLogs()`;
- returns structured evidence.

The Harness must bound this loop.

---

## 3.3 Skill

A Skill defines **how to perform a specialized task**.

Example folder:

```text
skills/
└── investigate-production-logs/
    ├── SKILL.md
    ├── scripts/
    │   ├── group_errors.py
    │   └── correlate_traces.py
    ├── references/
    │   └── logging-guide.md
    └── assets/
        └── investigation-template.md
```

Example Skill responsibility:

```text
1. Identify the affected service.
2. Get recent errors.
3. Group recurring exceptions.
4. Extract trace IDs.
5. Inspect relevant traces.
6. Correlate timestamps.
7. Separate evidence from hypotheses.
8. Never claim root cause without sufficient evidence.
```

**Skill = procedure.**  
**Tool = executable capability.**

---

# 4. Tools and MCP

## 4.1 Tool

A Tool is any executable capability available to an Agent.

It may be:

- a local C# method;
- an HTTP API call;
- database query;
- cloud SDK operation;
- CLI command;
- script;
- MCP-exposed tool.

Example:

```csharp
public async Task<IReadOnlyList<LogEntry>> GetRecentErrors(
    string service,
    CancellationToken ct)
{
    return await logAnalytics.GetRecentErrorsAsync(service, ct);
}
```

---

## 4.2 MCP

MCP provides a standardized way to expose tools/resources to agents.

```text
LogsAgent
   ↓
MCP Client
   ↓
Logs MCP Server
   ├── search_logs(query)
   ├── get_recent_errors(service)
   └── get_trace(trace_id)
   ↓
Azure Log Analytics / App Insights
```

Recommended MCP servers:

```text
Logs MCP
├── search_logs
├── get_recent_errors
└── get_trace

Database MCP
├── get_database_health
├── get_connection_pool
├── get_slow_queries
└── get_deadlocks

Metrics MCP
├── get_cpu
├── get_memory
├── get_latency
├── get_error_rate
└── get_request_rate

Deployment MCP
├── get_recent_deployments
├── get_release_diff
├── get_config_changes
└── get_deployment_status
```

**MCP = access mechanism.**  
**Tool = action exposed through that mechanism.**

---

# 5. RAG

RAG provides **organizational knowledge**, not live production state.

Recommended knowledge corpus:

```text
RAG Knowledge Base
├── Runbooks
├── Architecture docs
├── Service ownership docs
├── Troubleshooting guides
├── Postmortems
├── Known error catalog
├── Operational procedures
└── Deployment/change documentation
```

Recommended implementation:

```text
Agent
  ↓
SearchKnowledge(query)
  ↓
Azure AI Search / Vector Store
  ↓
Relevant chunks + metadata
  ↓
Agent reasoning
```

Example distinction:

```text
MCP / Tool:
"DB connections are currently 100/100."

RAG:
"The checkout runbook says pool exhaustion + SQL timeout usually indicates leaked connections."

Long-Term Memory:
"INC-123 had the same pattern three months ago."
```

---

# 6. Memory Architecture

## 6.1 Short-Term / Working Memory

Represents the current investigation.

```text
INC-123
├── affected services
├── evidence
├── hypotheses
├── completed tasks
├── open questions
├── current round
└── confidence
```

Recommended domain model:

```csharp
public sealed class InvestigationState
{
    public required string IncidentId { get; init; }
    public string Description { get; init; } = "";
    public int Round { get; set; }

    public List<AgentEvidence> AgentResults { get; } = [];
    public HashSet<string> CompletedAgents { get; } = [];
    public List<string> OpenQuestions { get; } = [];

    public string? CurrentRootCause { get; set; }
    public double RootCauseConfidence { get; set; }
}
```

This is the **canonical shared investigation state**.

---

## 6.2 Long-Term Memory

Long-term memory should be split into:

```text
Long-Term Memory
├── Semantic
│   └── learned facts / stable knowledge
│
├── Episodic
│   └── previous incidents and outcomes
│
└── Procedural
    └── reusable procedures / Skills
```

### Semantic example

```text
"checkout-api depends on orders-db"
```

### Episodic example

```text
"INC-123: v1.42 caused DB connection leak and was fixed by rollback + disposal fix"
```

### Procedural example

```text
"When investigating SQL timeouts, inspect recent errors → traces → pool saturation → deployments"
```

---

## 6.3 Retrieval Gate

Do not retrieve long-term memory on every turn.

```text
Agent request
   ↓
Retrieval Gate
   ↓
Does this task need memory?
   /            \
 NO             YES
 ↓               ↓
skip        retrieve relevant memories
```

Goal: reduce noise and token usage.

---

## 6.4 Memory Admission Gate

Do not persist every conversation detail.

```text
Candidate memory
      ↓
Admission Gate
      ↓
Useful in future?
  /           \
NO            YES
              ↓
       Long-Term Memory
```

---

## 6.5 Memory Consolidation

After an incident closes:

```text
Incident timeline
      ↓
Memory Consolidator
      ↓
extract durable knowledge
      ↓
Semantic Memory + Episodic Memory
```

---

# 7. Multi-Agent Architecture

## 7.1 Agents

Initial specialist set:

```text
TriageAgent
SupervisorAgent
LogsAgent
DatabaseAgent
MetricsAgent
DeploymentAgent
RootCauseAgent
RemediationAgent
```

Optional later agents:

```text
CodeAgent
SecurityAgent
KubernetesAgent
NetworkAgent
CostAgent
```

---

## 7.2 Responsibilities

### TriageAgent

- classify incident;
- identify affected domain/services;
- decide whether broad investigation is needed;
- choose first investigation strategy.

### SupervisorAgent

- high-level routing;
- choose specialists for another round;
- avoid unnecessary agents;
- coordinate unresolved questions;
- detect when human escalation is appropriate.

### LogsAgent

- inspect exceptions;
- correlate trace IDs;
- group recurring errors;
- build log evidence.

### DatabaseAgent

- connection pool;
- slow queries;
- locks/deadlocks;
- saturation;
- database-specific hypotheses.

### MetricsAgent

- CPU;
- memory;
- latency;
- throughput;
- error rate;
- resource saturation.

### DeploymentAgent

- release timeline;
- configuration changes;
- deployment diff;
- correlation with incident start.

### RootCauseAgent

- aggregate evidence;
- resolve contradictions;
- use RAG + long-term memory;
- produce structured root-cause analysis;
- generate confidence score.

### RemediationAgent

- propose immediate mitigation;
- propose safe remediation;
- identify risky/destructive actions;
- send all production-changing actions to approval.

---

# 8. Structured Agent Contracts

Avoid unstructured free-text communication between agents.

```csharp
public sealed record Evidence(
    string Type,
    string Description,
    string Source);

public sealed record Hypothesis(
    string Cause,
    double Confidence);

public sealed record AgentEvidence(
    string AgentName,
    IReadOnlyList<Evidence> Evidence,
    IReadOnlyList<Hypothesis> Hypotheses,
    IReadOnlyList<string> OpenQuestions);
```

Root-cause contract:

```csharp
public sealed record RootCauseAnalysis(
    string Cause,
    double Confidence,
    IReadOnlyList<string> SupportingEvidence,
    IReadOnlyList<string> Contradictions,
    IReadOnlyList<string> MissingEvidence);
```

Supervisor contract:

```csharp
public sealed record SupervisorDecision(
    IReadOnlyList<string> AgentsToRun,
    string Reason,
    bool InvestigationComplete);
```

---

# 9. Orchestration Patterns

## 9.1 Concurrent Orchestration

Used for the first broad investigation round.

```text
Incident
   ↓
┌─────────────┬─────────────┬─────────────┐
↓             ↓             ↓             ↓
LogsAgent   DBAgent    MetricsAgent   DeploymentAgent
└─────────────┴─────────────┴─────────────┘
                      ↓
               Evidence Bundle
```

Purpose: **breadth and speed**.

---

## 9.2 Handoff Orchestration

Used for focused deep-dive investigation.

```text
LogsAgent
   ↓
"DB-related evidence"
   ↓ handoff
DatabaseAgent
   ↓
"Need deployment context"
   ↓ handoff
DeploymentAgent
```

Purpose: **follow the strongest clue dynamically**.

Recommended topology:

```text
Triage
├── Logs
├── DB
├── Metrics
└── Deploy

Logs
├── DB
├── Metrics
└── Deploy

DB
├── Logs
└── Metrics

Metrics
├── Logs
└── DB

Deploy
├── Logs
└── DB
```

Do not allow arbitrary agent-to-agent routing.

---

# 10. Workflow

Workflow defines the complete deterministic process around agentic decisions.

```text
START
  ↓
Triage
  ↓
Concurrent Investigation
  ↓
Merge Evidence
  ↓
Root Cause Analysis
  ↓
Confidence >= threshold?
  ├── YES → Remediation Proposal
  │           ↓
  │       Human Approval
  │           ↓
  │         Action
  │
  └── NO → Deep Dive Handoff
              ↓
          Root Cause Analysis
              ↓
          confidence check
              ↓
           repeat / stop
```

Recommended confidence threshold initially:

```text
0.85
```

This threshold must later be calibrated using evals.

---

# 11. Multi-Agent Loop

```text
Investigation Round
       ↓
Specialists
       ↓
Evidence Merge
       ↓
RootCauseAgent
       ↓
Confidence check
   /           \
LOW            HIGH
 ↓               ↓
Supervisor    Remediation
 ↓
next agents / handoff
 ↓
next round
```

Termination conditions:

```text
STOP when any condition is true:

- confidence >= configured threshold;
- max investigation rounds reached;
- no meaningful new evidence found;
- safety policy requires human intervention;
- external dependency is unavailable;
- operator explicitly stops the investigation.
```

Recommended initial limits:

```text
Max investigation rounds: 3
Max agent iterations per invocation: 8-10
Max high-risk action attempts: 0 without approval
```

---

# 12. Conflict Resolution

Agents may disagree.

Example:

```text
LogsAgent:
"DB saturation likely" — confidence 0.90

DatabaseAgent:
"DB currently healthy" — confidence 0.80
```

RootCauseAgent must return:

```text
Contradiction detected.

Need:
- DB metrics at exact incident timestamp;
- connection pool history;
- correlated distributed trace.
```

Supervisor then launches only the necessary next checks.

Do not use majority voting as the default conflict resolver. Prefer **evidence-driven reconciliation**.

---

# 13. Harness Layer

Harness surrounds the entire agent/workflow system.

```text
HARNESS
├── Planning
├── Todo tracking
├── Session persistence
├── Context compaction
├── Tool-result trimming
├── Bounded loops
├── Timeouts
├── Retries
├── Cost/token budgets
├── Tool approvals
├── Checkpoints / resume
├── File workspace
└── Observability
```

---

## 13.1 Planning / Todos

Example:

```text
[✓] Identify affected service
[✓] Inspect logs
[✓] Inspect metrics
[ ] Inspect DB history
[ ] Compare latest deployment
[ ] Determine root cause
[ ] Propose remediation
```

This allows long-running work to remain resumable and understandable.

---

## 13.2 Context Compaction

Long incident investigations may produce huge context.

```text
Raw context
├── 50 tool calls
├── 20 traces
├── 300 log lines
└── multiple agent outputs

        ↓ compaction

Compact state
├── established facts
├── unresolved questions
├── current hypothesis
└── next actions
```

---

## 13.3 Tool Result Trimming

Old large tool results should be summarized after use.

```text
SearchLogs → 20,000 chars
       ↓
Agent consumes result
       ↓
Next prompt receives summary + references
instead of the full 20,000 chars
```

---

## 13.4 Approval Policy

Safe read operations:

```text
SearchLogs
GetTrace
GetMetrics
GetDeploymentHistory
QueryReadOnlyDatabaseHealth
```

Potentially destructive operations:

```text
RestartService
RollbackDeployment
ScaleCluster
DeleteResource
ExecuteWriteSql
KillPod
ChangeConfiguration
```

Policy:

```text
Read-only tools → automatic
Production-changing tools → explicit human approval
```

---

# 14. LLMOps Architecture

```text
LLMOps
├── Tracing
├── Evaluation
├── Monitoring
├── Prompt versioning
├── Skill versioning
├── Model versioning
├── Regression dataset
├── Deterministic evals
├── LLM-as-Judge
├── Cost tracking
├── Latency tracking
├── Turn receipts
├── Model arena
├── Release gate
└── Feedback loops
```

---

## 14.1 Evaluation Dimensions

### Output evals

- root-cause correctness;
- groundedness;
- completeness;
- relevance;
- confidence calibration;
- hallucination rate.

### Agent trajectory evals

- tool selection;
- unnecessary tool calls;
- loop efficiency;
- routing quality;
- successful handoff;
- correct termination.

### Operational evals

- latency;
- token usage;
- cost;
- retries;
- error rate;
- timeout rate.

---

## 14.2 Regression Dataset

Create a dataset of known incidents:

```text
Incident
Expected Root Cause
Required Evidence
Forbidden Claims
Expected Tools
Optional Tools
Expected Resolution
```

Example:

```text
INC-EVAL-001

Input:
checkout-api HTTP 500 after deployment

Expected root cause:
DB connection leak

Required evidence:
- SQL timeouts
- pool saturation
- deployment immediately before incident

Forbidden:
- claiming CPU saturation without evidence
```

---

## 14.3 Release Gate

```text
New Prompt / Skill / Model
          ↓
Regression Dataset
          ↓
Deterministic Evals
          +
LLM-as-Judge
          ↓
Quality / Safety / Cost thresholds
          ↓
        Release Gate
        /          \
      fail         pass
       ↓             ↓
     reject        deploy
```

---

## 14.4 Turn Receipt

Persist one receipt per significant agent run:

```text
RunId
IncidentId
Agent
Model
PromptVersion
SkillVersion
ToolCalls
TokensIn
TokensOut
Cost
Latency
MemoryRetrieved
MemoryStored
EvalScores
FinalStatus
```

This is essential for auditability and debugging.

---

# 15. Suggested .NET Solution Structure

```text
ProductionIncidentAI.sln
│
├── src/
│   │
│   ├── ProductionIncident.Api/
│   │   ├── Controllers/
│   │   ├── Endpoints/
│   │   └── Program.cs
│   │
│   ├── ProductionIncident.Application/
│   │   ├── Incidents/
│   │   ├── Workflows/
│   │   ├── Orchestration/
│   │   └── Contracts/
│   │
│   ├── ProductionIncident.Agents/
│   │   ├── Triage/
│   │   ├── Supervisor/
│   │   ├── Logs/
│   │   ├── Database/
│   │   ├── Metrics/
│   │   ├── Deployment/
│   │   ├── RootCause/
│   │   └── Remediation/
│   │
│   ├── ProductionIncident.Skills/
│   │   ├── investigate-logs/
│   │   ├── investigate-database/
│   │   ├── analyze-metrics/
│   │   ├── deployment-analysis/
│   │   └── root-cause-analysis/
│   │
│   ├── ProductionIncident.Mcp/
│   │   ├── Logs/
│   │   ├── Database/
│   │   ├── Metrics/
│   │   └── Deployment/
│   │
│   ├── ProductionIncident.Rag/
│   │   ├── Indexing/
│   │   ├── Retrieval/
│   │   └── Models/
│   │
│   ├── ProductionIncident.Memory/
│   │   ├── Working/
│   │   ├── Semantic/
│   │   ├── Episodic/
│   │   ├── RetrievalGate/
│   │   ├── AdmissionGate/
│   │   └── Consolidation/
│   │
│   ├── ProductionIncident.Harness/
│   │   ├── Planning/
│   │   ├── Todos/
│   │   ├── Context/
│   │   ├── Budgets/
│   │   ├── Approvals/
│   │   ├── Checkpoints/
│   │   └── Policies/
│   │
│   ├── ProductionIncident.LlmOps/
│   │   ├── Tracing/
│   │   ├── Evaluation/
│   │   ├── Receipts/
│   │   ├── ReleaseGates/
│   │   └── Monitoring/
│   │
│   └── ProductionIncident.Infrastructure/
│       ├── Azure/
│       ├── Persistence/
│       ├── Messaging/
│       └── Observability/
│
├── tests/
│   ├── UnitTests/
│   ├── IntegrationTests/
│   ├── AgentEvals/
│   ├── WorkflowTests/
│   └── RegressionEvals/
│
├── evals/
│   ├── datasets/
│   ├── expected-results/
│   └── scenarios/
│
├── skills/
│   └── ...
│
└── docs/
    ├── architecture.md
    ├── agents.md
    ├── mcp.md
    ├── rag.md
    └── runbooks/
```

---

# 16. Logical End-to-End Sequence

## Phase A — Incident ingestion

```text
1. Azure Monitor / user / webhook creates incident.
2. Gateway creates IncidentContext.
3. Harness creates/resumes investigation session.
4. Workflow begins.
```

---

## Phase B — Triage

```text
5. TriageAgent classifies incident.
6. Determine affected services/domains.
7. Create initial investigation plan/todos.
8. Select broad concurrent investigation.
```

---

## Phase C — Concurrent investigation

```text
9. LogsAgent starts.
10. DBAgent starts.
11. MetricsAgent starts.
12. DeploymentAgent starts.
13. Each uses Skill + Agent Loop + MCP tools.
14. Each returns structured AgentEvidence.
15. Evidence is written to shared InvestigationState.
```

---

## Phase D — Evidence aggregation

```text
16. Deterministic EvidenceAggregator merges results.
17. Deduplicate evidence.
18. Collect open questions.
19. Identify conflicts.
```

---

## Phase E — Root cause analysis

```text
20. RootCauseAgent receives shared evidence.
21. Retrieval Gate decides whether long-term memory is useful.
22. RAG retrieves relevant runbooks/docs/postmortems.
23. Long-term memory retrieves similar past incidents.
24. Optional MCP live checks validate unresolved facts.
25. RootCauseAgent returns structured RootCauseAnalysis.
```

---

## Phase F — Confidence routing

```text
26. Deterministic confidence router evaluates result.

IF confidence >= threshold:
    → remediation phase

ELSE:
    → deep-dive phase
```

---

## Phase G — Handoff deep dive

```text
27. Supervisor chooses best starting specialist.
28. Specialist follows strongest clue.
29. Agent may hand off to another specialist.
30. Shared state is updated with new evidence.
31. RootCauseAgent runs again.
32. Repeat until stop condition is met.
```

---

## Phase H — Remediation

```text
33. RemediationAgent proposes:
    - immediate mitigation;
    - permanent fix;
    - verification plan;
    - rollback plan.

34. Any production-changing action goes through approval.
35. Human approves/rejects.
36. Approved tool executes action.
37. Verification tools confirm system recovery.
```

---

## Phase I — Close and learn

```text
38. Incident is marked resolved.
39. Create final incident report.
40. Memory Admission Gate chooses durable learnings.
41. Memory Consolidator updates semantic/episodic memory.
42. Postmortem may be indexed into RAG.
43. Turn/run receipts are persisted.
44. Evals score the investigation.
```

---

# 17. Development Roadmap

## Milestone 1 — Single specialist agent

Implement:

```text
LogsAgent
+ local fake tools
+ structured output
+ agent loop
```

Done when:

- agent calls tools correctly;
- output deserializes into `AgentEvidence`;
- unit tests cover tool behavior.

---

## Milestone 2 — Four specialist agents

Implement:

```text
LogsAgent
DatabaseAgent
MetricsAgent
DeploymentAgent
```

Done when:

- each agent has clear instructions;
- each has domain-specific tools;
- outputs use common contract.

---

## Milestone 3 — Shared State + Concurrent orchestration

Implement:

```text
Concurrent first round
EvidenceAggregator
InvestigationState
```

Done when:

- agents execute concurrently;
- state contains all results;
- deterministic aggregation works.

---

## Milestone 4 — RootCauseAgent

Implement:

```text
RootCauseAnalysis
Confidence routing
Conflict detection
```

Done when:

- root cause is structured;
- contradictions and missing evidence are explicit;
- confidence is calibrated enough for dev use.

---

## Milestone 5 — Handoff deep dive

Implement:

```text
Supervisor
Handoff topology
Deep-dive workflow
Termination conditions
```

Done when:

- agents can transfer control;
- routing is constrained;
- loops terminate safely.

---

## Milestone 6 — Real MCP integrations

Replace fake tools with:

```text
Logs MCP → Azure Log Analytics / App Insights
DB MCP → SQL/Cosmos/PostgreSQL
Metrics MCP → Azure Monitor / Prometheus
Deploy MCP → GitHub / Azure DevOps / Kubernetes
```

Done when:

- live read-only investigation works in a test environment;
- auth is least-privilege;
- secrets are not exposed to LLM context.

---

## Milestone 7 — RAG

Implement:

```text
Azure AI Search
Runbook ingestion
Architecture doc ingestion
Postmortem ingestion
Metadata filters
```

Done when:

- agents retrieve relevant knowledge;
- citations/source IDs are preserved;
- retrieval quality has baseline evals.

---

## Milestone 8 — Memory

Implement:

```text
Short-term shared state
Semantic memory
Episodic memory
Retrieval Gate
Admission Gate
Consolidator
```

Done when:

- relevant past incidents can be recalled;
- irrelevant memories are filtered;
- new memories are admitted selectively.

---

## Milestone 9 — Harness

Implement:

```text
Planning
Todos
Session persistence
Compaction
Tool-result trimming
Budgets
Bounded loops
Retries/timeouts
Approvals
Checkpoint/resume
```

Done when:

- interrupted workflows can resume;
- runaway loops are prevented;
- destructive tools always require approval.

---

## Milestone 10 — LLMOps / Evaluation

Implement:

```text
Tracing
Turn receipts
Regression dataset
Deterministic evals
LLM-as-Judge
Cost/latency monitoring
Release gate
```

Done when:

- prompt/model/skill changes are regression-tested;
- bad releases can be blocked automatically;
- every incident run is auditable.

---

# 18. Security Principles

1. Agents should never receive raw long-lived credentials.
2. MCP servers authenticate independently and expose least-privilege operations.
3. Read-only investigation tools are separated from write/remediation tools.
4. Destructive operations require human approval.
5. Tool inputs must be validated server-side.
6. Production access must be logged and attributable.
7. Secrets must be stored in a secret manager such as Azure Key Vault.
8. Sensitive production data should be minimized before entering model context.
9. Each agent receives only the tools it requires.
10. Handoff routes must be explicitly allowed.

---

# 19. Observability Requirements

Every run should expose:

```text
IncidentId
RunId
AgentId
WorkflowStep
Model
Prompt/Skill versions
Tool calls
Tool latency
Agent latency
Tokens
Cost
Handoffs
Retries
Errors
Confidence
Eval scores
Final result
```

Use OpenTelemetry-compatible traces where possible.

---

# 20. Non-Functional Requirements

## Reliability

- investigation must survive transient tool failures;
- workflow state should be resumable;
- tools must be idempotent where actions are possible.

## Performance

- initial specialists should run concurrently;
- avoid duplicate retrieval/tool calls;
- trim large tool outputs;
- cache safe RAG retrievals where appropriate.

## Safety

- no automatic destructive production changes;
- explicit max loops/rounds;
- explicit confidence threshold;
- operator kill switch.

## Auditability

- every tool call and agent decision must be traceable;
- store model/prompt/skill versions for reproduction.

---

# 21. First Development Slice

The smallest useful implementation should be:

```text
ASP.NET Core endpoint
      ↓
INC-123
      ↓
Concurrent:
LogsAgent + DBAgent + MetricsAgent
      ↓
InvestigationState
      ↓
RootCauseAgent
      ↓
RootCauseAnalysis
```

Use fake/static tool implementations first.

Do **not** start with MCP, RAG, memory, Harness and LLMOps all at once.

Recommended sequence:

```text
1. Agents + tools
2. Structured outputs
3. Shared state
4. Concurrent orchestration
5. Root cause
6. Handoff
7. MCP
8. RAG
9. Memory
10. Harness
11. Evals / LLMOps
12. Remediation approvals
```

---

# 22. Mental Model

```text
Agent       = decides
Skill       = knows how
Tool        = executes
MCP         = exposes/accesses capabilities
RAG         = retrieves organizational knowledge
Memory      = remembers previous/current experience
Agent Loop  = tool-use reasoning cycle
Orchestration = coordinates multiple agents
Concurrent  = specialists work in parallel
Handoff     = one agent transfers control to another
Workflow    = end-to-end process
Harness     = operational control around the process
LLMOps      = evaluates, observes and safely evolves the system
```

---

# 23. Final Development Target

```text
Production Incident
      ↓
Harness-controlled Workflow
      ↓
Concurrent specialist investigation
      ↓
Shared evidence/state
      ↓
RootCauseAgent + RAG + Memory
      ↓
Confidence router
      ↓
Deep-dive Handoff if needed
      ↓
Root cause confirmed
      ↓
Remediation proposal
      ↓
Human approval
      ↓
Controlled production action
      ↓
Verification
      ↓
Memory consolidation + Postmortem + Evals
```

This is the target architecture to evolve toward incrementally.
