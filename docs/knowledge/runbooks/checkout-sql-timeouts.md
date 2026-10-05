---
id: runbook:checkout-sql-timeouts
title: Runbook — checkout-api SQL timeouts
kind: runbook
tags: [checkout-api, orders-db, sql, timeout, connection-pool]
---
# Runbook — checkout-api SQL timeouts

## Symptoms
HTTP 500 on POST /api/checkout, `SqlException: Timeout expired ... prior to obtaining a connection from the pool`.
p99 latency close to 30 s (the SQL connection timeout).

## Diagnosis
Pool exhaustion combined with SQL timeouts usually indicates leaked connections, not a slow database.
1. Check the connection pool history of orders-db at the incident timestamp (not only the current snapshot).
2. Look for many idle (sleeping) sessions held by checkout-api: connections opened but never returned.
3. Compare with the latest deployment: look for missing `using` / `await using` / `Dispose` around `SqlConnection`.
4. If the database CPU is high and queries are slow, follow the slow-query runbook instead.

## Mitigation
- If a recent release introduced the leak: roll back checkout-api to the previous version.
- A rolling restart only releases the leaked connections temporarily; the pool fills up again.

## Owner
team-checkout (#checkout-oncall)
