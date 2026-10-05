# RAG and memory

| Source | Answers | Implementation |
|---|---|---|
| MCP tools | "DB connections are currently 100/100." | live state |
| RAG (`search_knowledge`) | "Pool exhaustion + SQL timeout usually means leaked connections." | `IKnowledgeBase` — BM25 in-memory (dev), Azure AI Search (prod) |
| Long-term memory (`recall_similar_incidents`) | "INC-087 had the same pattern." | `ILongTermMemory` — semantic + episodic |

## Corpus

`docs/knowledge/**.md` (runbooks, architecture, postmortems, known errors) is embedded and indexed at startup.
Front matter: `id`, `title`, `kind`, `tags`. Documents are chunked by `##` section; hits keep their `sourceId`
so agents can cite them. Resolved, verified incidents are indexed back as `postmortem:{IncidentId}`.

## Memory lifecycle

- **Retrieval gate** — only RootCause and Remediation can recall memory, and only when there is something to compare.
- **Admission gate** — an episode is stored only if recovery was verified, confidence ≥ 0.85 and it is new; facts
  are deduplicated.
- **Consolidator** — after closing: episode (summary, root cause, resolution, signals) + `service depends on db`
  facts observed in evidence.
- **Procedural memory** — the versioned Skills.
