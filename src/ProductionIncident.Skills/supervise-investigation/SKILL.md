---
name: supervise-investigation
version: 1.0.0
description: Decide which specialists run next when root-cause confidence is low.
---
# Supervise the investigation

1. Read the root-cause analysis: confidence, contradictions and missing evidence.
2. Map each missing fact to the single specialist that can obtain it.
3. Choose the minimum set of specialists — do not re-run agents whose evidence is complete.
4. Set `investigationComplete=true` (escalate to a human) when no specialist can obtain the
   missing evidence, or the same gaps persisted for two rounds.
5. Always explain the reason in one sentence.
