---
id: "b1-scaffold-e2e-tests"
title: "Scaffold E2E.Tests project"
group: "B"
sequence: 1
repo: "."
base_branch: "main"
depends_on: ["a2-implement-dummy-server-transports"]
importance: 3
complexity: 2
security_critical: false
production_touching: false
model_hint: "fast"
taskflow_refs: ["02-target-architecture.md"]
---

## Goal
Create a new C# xUnit test project named `McpPlugin.E2E.Tests` to house the orchestrator and integration tests.

## Scope & seams
- Create `McpPlugin.E2E.Tests` xUnit project.
- Add it to `McpPlugin.sln`.
- Ensure it is picked up by `dotnet test` in the GitHub Actions CI workflow (the current glob `TestResults/**/*.trx` and the generic `dotnet test` should automatically cover it, but confirm).

## Definition of Done
- `McpPlugin.E2E.Tests` compiles and runs successfully with 0 or a dummy green test.
- Project is part of the solution.
