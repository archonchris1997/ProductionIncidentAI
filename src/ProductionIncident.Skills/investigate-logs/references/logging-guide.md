# Logging guide

- Every log entry carries `service`, `level`, `traceId`, `spanId`, `timestamp` (UTC) and `exception.type`.
- SQL client errors are logged as `Microsoft.Data.SqlClient.SqlException` with the original message.
- "Timeout expired ... prior to obtaining a connection from the pool" means the ADO.NET connection
  pool was exhausted — this is different from a slow query (`Execution Timeout Expired`).
- Trace IDs are W3C trace-context ids, prefixed with `t-` in the fake environment.
