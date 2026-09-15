---
id: "a2-implement-dummy-server-transports"
title: "Implement DummyServer transports and logging"
group: "A"
sequence: 2
repo: "."
base_branch: "main"
depends_on: ["a1-scaffold-dummy-server"]
importance: 6
complexity: 5
security_critical: false
production_touching: false
model_hint: "mid"
taskflow_refs: ["02-target-architecture.md"]
---

## Goal
Implement the MCP protocol in `DummyServer` using the existing `McpPlugin.Server` library, configure it to listen on `stdio` or `sse`, and write structured test logs.

## Scope & seams
- Reference `McpPlugin.Server` in `DummyServer`.
- Start an MCP Server instance based on the `--transport` argument.
- Expose a single tool `ping` that returns `pong`.
- Write `[EVENT] Connected`, `[EVENT] Received: initialize`, and `[EVENT] Tool Call: ping` to the file specified by `--log-file`.

## Definition of Done
- Server successfully initializes over `stdio` and `sse`.
- Structured events are appended to the specified log file upon client connection, initialization, and tool invocation.
