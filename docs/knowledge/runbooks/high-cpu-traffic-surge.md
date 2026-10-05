---
id: runbook:high-cpu-traffic-surge
title: Runbook — CPU saturation and traffic surges
kind: runbook
tags: [cpu, saturation, traffic, scaling, search-api]
---
# Runbook — CPU saturation and traffic surges

## Symptoms
Timeouts, thread pool starvation warnings, CPU above 90% on all replicas, request rate well above baseline.

## Diagnosis
1. Compare the request rate with its baseline. A multiple of the baseline means the incident is load-driven.
2. Check that no deployment or configuration change happened in the window (rules out a regression).
3. Traces dominated by CPU-bound spans (no I/O wait) confirm compute saturation.

## Mitigation
- Scale out the service (e.g. 3x replicas for a 3x surge). Scaling is reversible.
- Check that the HPA max replica count is not the bottleneck.

## Owner
team-search (#search-oncall)
