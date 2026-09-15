# E2E Testing Roadmap

## Waves

### Wave 1: The Dummy Server
Create the observable server that agents will connect to.
- Scaffold `McpPlugin.DummyServer` project.
- Implement basic `stdio` and `sse` transports.
- Implement `--log-file` argument and write structured `[EVENT]` lines on `initialize` and tool calls.

### Wave 2: The Orchestrator Foundation
Create the test harness to manage the lifecycle.
- Scaffold `McpPlugin.E2E.Tests` project.
- Implement Sandbox manager (creates unique temp dirs, handles teardown).
- Implement log parser (to read the DummyServer log file and expose assertions).

### Wave 3: Antigravity Agent Tests
Write the actual tests for the Antigravity CLI.
- Implement agent adapter for Antigravity (downloads/locates executable).
- Implement config generator (writes `antigravity.json`).
- Write `Antigravity_Connects_Via_Stdio` test.
- Write `Antigravity_Connects_Via_Sse` test.

### Wave 4: CloudCode Agent Tests (Future)
- Implement agent adapter for VS Code / CloudCode headless testing.

## Task Board

| Task (spec) | needs | repo/base | imp/cx | model | Status | Run / PR | Updated |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Scaffold DummyServer (`a1`) | | MCP-Plugin-dotnet | 3/2 | fast | Pending | | |
| Implement Transports (`a2`) | a1 | MCP-Plugin-dotnet | 6/5 | mid | Pending | | |
| Scaffold E2E.Tests (`b1`) | a2 | MCP-Plugin-dotnet | 3/2 | fast | Pending | | |
| Implement Sandbox (`b2`) | b1 | MCP-Plugin-dotnet | 5/5 | mid | Pending | | |
| Antigravity Tests (`b3`) | b2 | MCP-Plugin-dotnet | 7/7 | mid | Pending | | |

## Progress Log

- **2026-09-15:** Taskflow plan generated and reviewed. No P0/P1/P2 structural findings. Owner decisions D1 and D2 approved. Proceeding to task creation.
- **2026-09-15:** Created immutable task specifications in `tasks/`. Task board populated. Ready for execution.
