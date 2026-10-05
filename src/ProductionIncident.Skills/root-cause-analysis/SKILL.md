---
name: root-cause-analysis
version: 1.0.0
description: Reconcile specialist evidence into a structured, calibrated root-cause analysis.
---
# Root-cause analysis

1. Read the established facts, hypotheses, conflicts and open questions.
2. Build a causal chain: trigger → mechanism → symptom. Every link must cite evidence.
3. Resolve contradictions with evidence, never by majority vote. A "currently healthy" snapshot
   does not contradict a historical saturation at incident time — say so explicitly.
4. Use `search_knowledge` for runbooks/known errors and `recall_similar_incidents` for past
   incidents with the same signals. Treat them as context, not proof of the current incident.
5. If a key fact is unresolved, run a live read-only check (logs/db/metrics/deploy tools).
6. Confidence rubric:
   - ≥ 0.85: trigger, mechanism and symptom all evidenced; no unresolved contradiction.
   - 0.60–0.84: plausible chain with one missing link → list it in `missingEvidence`.
   - < 0.60: competing hypotheses or contradictions remain.
7. List contradictions and missing evidence explicitly; empty lists only if there are none.
