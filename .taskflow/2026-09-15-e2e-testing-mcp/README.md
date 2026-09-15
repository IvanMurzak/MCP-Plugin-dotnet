# E2E Testing for AI Agents (Cross-Platform)

## Problem
The project lacks End-to-End (E2E) tests to verify that supported AI agents (e.g., Antigravity, CloudCode) can correctly configure and connect to an MCP Server across different operating systems. This gap hides OS-specific bugs (such as path resolution or stdio piping issues on macOS/Windows) in the agent's configuration.

## Status
Planning. Architecture design and implementation roadmap in progress.

## Decisions

- **D1 [APPROVED 2026-09-15]:** Which testing framework should orchestrate the E2E tests?
  - *Decision:* Option A (C# / `xUnit`). Keeps the tests within the .NET ecosystem, reusing existing CI pipelines. We can use `System.Diagnostics.Process` to drive CLI tools like Antigravity.
- **D2 [APPROVED 2026-09-15]:** How should we distribute the Dummy MCP Server for testing?
  - *Decision:* Option A (Build a tiny `McpPlugin.DummyServer` C# project in this repo). Keeps the demo code clean and provides a dedicated, highly observable target for testing.

## Summary
To ensure agents connect properly over both `stdio` and `sse` transports, we will introduce a "Dummy MCP Server" that explicitly logs its protocol lifecycle (e.g., `initialize`, tool invocations). A Test Orchestrator will provision an isolated sandbox (a temp directory), generate the specific configuration required by the AI agent, launch the agent, and verify success by parsing the Dummy Server's logs. The tests will run natively on GitHub Actions Hosted Runners (Windows, macOS, Ubuntu) to catch true OS-level bugs without relying on Docker.

## Document Map
- [01-current-architecture.md](01-current-architecture.md): Existing CI setup and limitations.
- [02-target-architecture.md](02-target-architecture.md): Dummy Server design and Test Harness flow.
- [ROADMAP.md](ROADMAP.md): Implementation steps and progress.
