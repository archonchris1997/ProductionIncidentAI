---
name: remediation-planning
version: 1.0.0
description: Propose safe mitigation, permanent fix, verification and rollback plans.
---
# Remediation planning

1. Prefer the least risky mitigation that restores service (rollback of a bad release before
   restarts or scaling).
2. Every production-changing step is a `ProposedAction` with the exact tool name and arguments.
   You never execute it: it goes to human approval.
3. Classify risk: `low` (reversible, no data impact), `medium` (brief disruption), `high`
   (irreversible or data-affecting). Never propose `execute_write_sql` or `delete_resource`
   unless the evidence makes it unavoidable, and mark it `high`.
4. Permanent fix: the code/config change that removes the cause.
5. Verification plan: the read-only checks (metrics, logs, db) that prove recovery.
6. Rollback plan: how to undo the mitigation itself.
