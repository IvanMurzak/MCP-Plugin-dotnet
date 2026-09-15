---
id: "a1-scaffold-dummy-server"
title: "Scaffold DummyServer project"
group: "A"
sequence: 1
repo: "."
base_branch: "main"
depends_on: []
importance: 3
complexity: 2
security_critical: false
production_touching: false
model_hint: "fast"
taskflow_refs: ["02-target-architecture.md"]
---

## Goal
Create a minimal C# Console Application project named `McpPlugin.DummyServer` and add it to the existing `McpPlugin.sln` solution.

## Scope & seams
- Create a new .NET 8/9 console app in a new `McpPlugin.DummyServer` directory.
- Add it to the solution file.
- It does not need the full MCP logic yet, just a main entry point that can parse the `--transport` and `--log-file` arguments.

## Definition of Done
- `McpPlugin.DummyServer` project is created.
- Project is added to `McpPlugin.sln`.
- Application can be built and run.
- Main method accepts and parses `--transport` and `--log-file` arguments.
