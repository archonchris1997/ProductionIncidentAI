# Eval scenarios

Each regression case in `../datasets/regression.json` is backed by a deterministic scenario of the fake
production estate (`src/ProductionIncident.Mcp/Backends/Fake/FakeScenarios.cs`):

| Case | Service | Scenario | What it exercises |
|------|---------|----------|-------------------|
| INC-EVAL-001 | checkout-api | DB connection leak introduced by v1.42 | concurrent round → conflict (logs vs "DB healthy" snapshot) → low confidence → supervisor → handoff DeploymentAgent → DatabaseAgent → confirmed root cause → rollback approval |
| INC-EVAL-002 | search-api | CPU saturation from a 3.1x traffic surge | one-round resolution, no false "release regression" claim, scale-out proposal |
| INC-EVAL-003 | payments-api | `PaymentGateway:TimeoutMs` lowered 5000 → 500 | config-change correlation, config revert proposal |

To add a case: add a `FakeScenario` (or point the MCP servers at a recorded environment), add the case to the
dataset with required evidence, forbidden claims and expected tools, and run
`dotnet test tests/ProductionIncident.RegressionEvals`.
