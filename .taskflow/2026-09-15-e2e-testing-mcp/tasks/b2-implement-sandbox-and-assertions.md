---
id: "b2-implement-sandbox-and-assertions"
title: "Implement Sandbox and log parsing"
group: "B"
sequence: 2
repo: "."
base_branch: "main"
depends_on: ["b1-scaffold-e2e-tests"]
importance: 5
complexity: 5
security_critical: false
production_touching: false
model_hint: "mid"
taskflow_refs: ["02-target-architecture.md"]
---

## Goal
Provide reusable testing utilities in `E2E.Tests` for creating temporary isolated directories (sandboxes) and parsing `DummyServer` logs.

## Scope & seams
- Implement a `Sandbox` class that creates a temp directory in `Path.GetTempPath()`, provides a clean environment for config files, and implements `IDisposable` to recursively delete the folder on teardown.
- Implement a `LogParser` class that reads the DummyServer log file and provides assertion methods like `AssertContainsEvent(string eventName)`.

## Definition of Done
- Tests can cleanly instantiate and dispose a `Sandbox`.
- Log contents can be verified programmatically without flakiness (e.g., handling file locks properly).
