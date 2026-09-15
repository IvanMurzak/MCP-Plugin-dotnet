# Target Architecture

To enable reliable, cross-platform E2E testing of AI agents connecting to our MCP plugin, we will introduce two new components:

## 1. Dummy MCP Server (`McpPlugin.DummyServer`)
A minimal C# console application acting as the target for the AI agents.
- **Transports:** Supports `--transport stdio` and `--transport sse --port <port>`.
- **Tools:** Exposes a simple `ping` tool that returns `pong`.
- **Observability:** Crucially, it accepts a `--log-file <path>` argument. It will append structured lifecycle events to this file:
  - `[EVENT] Connected`
  - `[EVENT] Received: initialize`
  - `[EVENT] Tool Call: ping`
This log file acts as the source of truth for the tests, treating the agent itself as a black box.

## 2. E2E Test Orchestrator
A test harness (likely built into a new C# test project like `McpPlugin.E2E.Tests`) that manages the sandbox and executes the tests.

### Execution Flow for a Single Test
1. **Sandbox Provisioning:** Create a unique temporary directory (e.g., `C:\temp\run-123\`).
2. **Agent Setup:** Install or locate the latest AI agent executable.
3. **Configuration Generation:** Generate the required config file (e.g., `antigravity.json`) inside the sandbox.
   - For *SSE*, the config points to `http://localhost:<dynamic_port>`.
   - For *STDIO*, the config defines the command `dotnet McpPlugin.DummyServer.dll --transport stdio --log-file <sandbox_path>\dummy.log`.
4. **Server Startup (SSE only):** The orchestrator starts the DummyServer on a free port.
5. **Agent Execution:** The orchestrator launches the agent as a child process (e.g., `agy run "Call the ping tool"`) with environment variables pointing to the sandbox config.
6. **Assertion:** The orchestrator waits for the agent to exit (or times out), then reads the `<sandbox_path>\dummy.log` file to verify the presence of the `initialize` and `ping` events.
7. **Teardown:** Kill any orphaned processes and delete the temporary directory.

## CI/CD Integration
The E2E tests will run natively in the existing GitHub Actions matrix (`ubuntu-latest`, `windows-latest`, `macos-latest`). This native execution provides total OS isolation per run, ensuring port availability and accurate testing of OS-specific path and pipe behaviors.
