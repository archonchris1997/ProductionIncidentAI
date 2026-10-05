---
name: triage-incident
version: 1.0.0
description: Classify an incident and choose the first investigation strategy.
---
# Triage

1. Classify the category: availability, latency, data, security, capacity, other.
2. Assess severity (SEV1 customer-facing outage … SEV4 minor).
3. Identify affected services from the title/description; never invent service names.
4. Choose specialists for the first round. Default to all four (logs, database, metrics,
   deployment) unless the incident is clearly scoped to one domain.
5. Strategy is `broad` (concurrent first round) or `focused` (single specialist first).
