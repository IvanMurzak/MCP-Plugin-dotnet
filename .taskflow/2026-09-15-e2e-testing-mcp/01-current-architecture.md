# Current Architecture

## CI/CD and OS Coverage
The repository currently utilizes GitHub Actions with a matrix strategy that covers `ubuntu-latest`, `windows-latest`, and `macos-latest` (`.github/workflows/test-pull-request.yml:10-17`).
This native OS execution is highly beneficial for testing OS-specific behavior (e.g., file permissions, pipes, path handling), bypassing the limitations of Docker containers which would restrict us to Linux environments on macOS/Windows hosts.

## Existing Testing
Tests are currently focused on unit and lower-level integration (e.g., `McpPlugin.Tests`, `McpPlugin.Server.Tests`), executed via standard `dotnet test`.

## E2E Limitations
There is currently no mechanism to test the integration from the perspective of an **external AI Agent** (like Antigravity or CloudCode). 
- We do not have a dedicated "Dummy Server" that outputs deterministic, verifiable logs for a test harness to assert against.
- We do not have automation to download agents, generate their specific configuration files (e.g., `antigravity.json`), and execute them headlessly.
- As a result, configuration regressions (e.g., malformed paths or broken stdio pipes in the agent's connection logic) are only caught through manual testing.
