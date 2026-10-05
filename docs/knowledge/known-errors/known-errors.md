---
id: known-errors:catalog
title: Known error catalog
kind: known-error
tags: [known-errors, sql, timeout, threadpool]
---
# Known error catalog

## SqlException — prior to obtaining a connection from the pool
The ADO.NET pool reached Max Pool Size and requests waited longer than the connect timeout.
Typical causes: connection leak (connections not disposed), long transactions holding connections, a sudden
increase in concurrency. Not caused by slow queries alone.

## TaskCanceledException — HttpClient.Timeout elapsing
The outbound HTTP call exceeded the configured client timeout. Compare the dependency latency with the timeout
value; check for recent configuration changes.

## ThreadPoolStarvation
The .NET thread pool cannot keep up: usually CPU saturation or sync-over-async blocking.
