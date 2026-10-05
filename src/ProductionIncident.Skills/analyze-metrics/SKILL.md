---
name: analyze-metrics
version: 1.0.0
description: Analyze service metrics (error rate, latency, CPU, memory, throughput) around the incident.
---
# Analyze metrics

1. Get the error rate for the affected service and find when it started deviating from baseline.
2. Get latency (p50/p99). Latency close to a client timeout suggests waiting on a dependency.
3. Get CPU and memory. Explicitly state when they are normal — this rules out resource saturation.
4. Get request rate to distinguish load-driven incidents from regressions.
5. Report each signal with baseline vs incident values and timestamps.
6. Do not claim CPU or memory saturation unless the numbers show it.

Evidence type to use: `metric`.
