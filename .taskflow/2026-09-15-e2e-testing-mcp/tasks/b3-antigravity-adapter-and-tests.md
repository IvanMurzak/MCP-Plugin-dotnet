---
id: "b3-antigravity-adapter-and-tests"
title: "Antigravity Adapter and E2E Tests"
group: "B"
sequence: 3
repo: "."
base_branch: "main"
depends_on: ["b2-implement-sandbox-and-assertions"]
importance: 7
complexity: 7
security_critical: false
production_touching: false
model_hint: "mid"
taskflow_refs: ["02-target-architecture.md"]
---

## Goal
Implement the test adapter for the Antigravity CLI and write the actual E2E xUnit tests to verify `stdio` and `sse` connections.

## Scope & seams
- Implement `AntigravityAdapter`: 
  - Generates `antigravity.json` configuration inside the sandbox pointing to DummyServer.
  - Launches `agy` via `System.Diagnostics.Process` in a headless mode (or similar test command).
  - Configures `HOME`/`USERPROFILE` environment variables so `agy` only reads the sandbox config.
- Write `Antigravity_Connects_Via_Stdio` test.
- Write `Antigravity_Connects_Via_Sse` test (which also starts DummyServer on a free port).

## Definition of Done
- `AntigravityAdapter` can launch the agent.
- `stdio` and `sse` tests are written and execute successfully on the developer's machine (and inherently in CI).
- The DummyServer log correctly records `initialize` and the `ping` tool invocation.
