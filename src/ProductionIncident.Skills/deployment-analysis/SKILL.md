---
name: deployment-analysis
version: 1.0.0
description: Correlate deployments and configuration changes with the incident timeline.
---
# Deployment analysis

1. List recent deployments for the affected service (`get_recent_deployments`).
2. Compute the gap between the latest deployment and the incident start.
3. Check configuration changes in the same window (`get_config_changes`) when relevant.
4. Broad round: build the timeline only and flag a suspect release as an open question.
   Deep dive: inspect the release diff (`get_release_diff`) between the previous and current versions
   and look for changes matching the other evidence (resource disposal, connection handling, timeouts, retries).
   If the diff points to a database effect, hand off to `DatabaseAgent` to confirm it.
5. Record the previous known-good version: remediation may need it for a rollback.

Evidence types to use: `deployment`, `code-change`, `config`.
