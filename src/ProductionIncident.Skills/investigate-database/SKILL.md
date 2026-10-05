---
name: investigate-database
version: 1.0.0
description: Investigate database health, connection pools, slow queries and locks.
---
# Investigate the database

1. Identify the database used by the affected service (incident context / architecture facts).
2. Check current health (`get_database_health`). A healthy *current* snapshot does NOT prove the
   database was healthy at incident time.
3. When logs suggest pool exhaustion or the incident is in the past, check the connection pool
   history over the incident window (`get_connection_pool`). Look for saturation and for idle
   sessions held by a single client application (typical of a connection leak).
4. Check slow queries (`get_slow_queries`) and deadlocks (`get_deadlocks`) only if latency or
   locking is suspected.
5. Report numbers with timestamps (e.g. "active connections 100/100 from 14:04 to 14:31").
6. If the database evidence points to an application-side change, hand off to `DeploymentAgent`
   (deep-dive mode only).

Evidence types to use: `database`, `connection-pool`, `query`.
