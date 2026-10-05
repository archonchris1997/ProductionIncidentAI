---
name: investigate-logs
version: 1.0.0
description: Investigate production logs and distributed traces to build log evidence.
---
# Investigate production logs

1. Identify the affected service from the incident context.
2. Get recent errors for that service (`get_recent_errors`).
3. Group recurring exceptions; note counts and first/last seen timestamps.
4. Extract trace IDs from the most frequent error group.
5. Inspect at least one relevant trace (`get_trace`) to find the slow or failing span.
6. Correlate timestamps with the incident start time.
7. Use `search_logs` only for a targeted question (e.g. a specific exception text).
8. Separate evidence (what the tools returned) from hypotheses (what it might mean).
9. Never claim a root cause without sufficient evidence; put gaps in `openQuestions`.

Evidence types to use: `log`, `trace`.
See `references/logging-guide.md` for log field conventions.
