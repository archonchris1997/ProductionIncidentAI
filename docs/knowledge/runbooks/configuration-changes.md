---
id: runbook:configuration-changes
title: Runbook — incidents caused by configuration changes
kind: runbook
tags: [config, configuration, timeout, payments-api, config-sync]
---
# Runbook — incidents caused by configuration changes

## Symptoms
Errors start minutes after a configuration reload, without any deployment. Client-side timeouts or
connection errors to a dependency; latency capped at the new timeout value.

## Diagnosis
1. List configuration changes in the window and compare old/new values.
2. Compare dependency latency (traces) with the configured timeout.

## Mitigation
- Revert the configuration key to its previous value with change_configuration.
- Disable the automatic config-sync job for the key until the change is reviewed.

## Owner
platform team (#platform-oncall)
