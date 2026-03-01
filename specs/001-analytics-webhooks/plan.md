# Implementation Plan: Analytics Webhooks for McpPlugin.Server

**Branch**: `001-analytics-webhooks` | **Date**: 2026-03-01 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-analytics-webhooks/spec.md`

## Summary

Add configurable analytics webhooks to McpPlugin.Server that fire HTTP POST requests on MCP events (tool calls, prompt retrievals, resource accesses, client connections/disconnections). Configuration is done entirely through launch arguments (`DataArguments`) and environment variables — no code changes required by operators. Webhooks are fire-and-forget, async, non-blocking, with a shared security token sent as a configurable HTTP header. Each webhook payload includes a UTC timestamp, event type discriminator, and schema version for future evolution.

## Technical Context

**Language/Version**: C# / .NET 8.0 + .NET 9.0 (server targets). `McpPlugin.Server`: LangVersion 9.0 (no C# 10+ features such as record structs, global usings, `required` properties, or extended property patterns). `McpPlugin.Server.Tests`: LangVersion 11.0 (tests may use C# 11 features).
**Primary Dependencies**: ASP.NET Core (Kestrel, SignalR), ModelContextProtocol SDK 1.0.0, R3 1.3.0, System.Text.Json, NLog, Microsoft.Extensions.DependencyInjection, com.IvanMurzak.ReflectorNet 3.12.1
**Storage**: N/A (stateless fire-and-forget delivery)
**Testing**: xUnit + Shouldly + Moq, `[Collection("McpPlugin.Server")]` isolation, `net8.0` + `net9.0`
**Target Platform**: Cross-platform .NET server (Linux/Windows/macOS)
**Project Type**: NuGet library (McpPlugin.Server bridge/gateway)
**Performance Goals**: Webhook delivery adds ≤10 ms p99 to tool call round-trip time
**Constraints**: Async non-blocking delivery, no retry logic, fire-and-forget, JSON payloads (UTF-8), configurable HTTP timeout (default 10s)
**Scale/Scope**: 4 webhook event categories, ~15 new source files in McpPlugin.Server, ~10 new test files

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### I. Bridge Architecture — PASS

Webhooks are an outbound concern of `McpPlugin.Server` only. No changes to the bridge pattern, SignalR transport, or client library. The hub endpoint remains `/hub/mcp-server`. No .NET app is spawned as a subprocess.

### II. Package-First Design — PASS

All webhook code lives in `McpPlugin.Server` (net8.0/net9.0). No new NuGet package is introduced. `McpPlugin.Common` gains only serialization-friendly DTO additions (if any) that remain free of ASP.NET Core dependencies. The `McpPlugin` client library is not modified.

### III. Attribute-Based Registration — N/A

Webhooks are server-side infrastructure, not tool/prompt/resource definitions. No attribute changes.

### IV. Test-First Discipline — PASS

All webhook services will follow Red-Green-Refactor. Tests will use xUnit + Shouldly + Moq. Async tests will use `await using` for disposable resources. No `SubscribeAwait` usage.

### V. Code Style & Reactive Patterns — PASS

- Namespace: `com.IvanMurzak.McpPlugin.Server.Webhooks.*`
- File headers: ASCII art Apache-2.0 license block
- Braces: Allman style
- Fields: `_camelCase` private, `PascalCase` public
- Events: R3 not required for outbound HTTP fire-and-forget; will use standard async/await with `IHttpClientFactory`
- Disposal: `CompositeDisposable` if subscriptions needed
- Logging: `Microsoft.Extensions.Logging` (NLog backend)
- DI: `Microsoft.Extensions.DependencyInjection`
- CLI args: `DataArguments` for all webhook configuration

**Pre-Phase 0 Gate: PASSED — no violations.**

## Project Structure

### Documentation (this feature)

```text
specs/001-analytics-webhooks/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
McpPlugin.Server/src/
├── Webhooks/
│   ├── Config/
│   │   └── WebhookOptions.cs                 # Configuration model parsed from DataArguments
│   ├── Models/
│   │   ├── WebhookPayload.cs                 # Envelope: timestamp, eventType, schemaVersion, data
│   │   ├── ToolCallEvent.cs                  # Tool call analytics event
│   │   ├── PromptEvent.cs                    # Prompt retrieval event
│   │   ├── ResourceEvent.cs                  # Resource access event
│   │   └── ConnectionEvent.cs                # Connection lifecycle event
│   ├── Services/
│   │   ├── IWebhookDispatcher.cs             # Dispatch interface
│   │   ├── WebhookDispatcher.cs              # HttpClient-based fire-and-forget delivery
│   │   ├── IWebhookEventCollector.cs         # Event collection interface
│   │   └── WebhookEventCollector.cs          # Collects events from routers/hub, dispatches
│   └── Extensions/
│       └── WebhookServiceExtensions.cs       # DI registration: AddWebhooks(DataArguments)
├── Extension/
│   ├── ExtensionsMcpServer.cs                # Modified: wire webhook collector into router handlers
│   └── ExtensionsMcpServerBuilder.cs         # Modified: call AddWebhooks(dataArguments)
├── Hub/
│   └── McpServerHub.cs                       # Modified: emit connection events to collector
└── Mcp/
    ├── Tool/ToolRouter.Call.cs               # Existing: tool routing entrypoint (wrapped via ExtensionsMcpServer)
    ├── Prompt/PromptRouter.Get.cs            # Existing: prompt routing entrypoint (wrapped via ExtensionsMcpServer)
    └── Resource/ResourceRouter.Read.cs       # Existing: resource routing entrypoint (wrapped via ExtensionsMcpServer)

McpPlugin.Common/src/
├── Utils/
│   └── DataArguments.cs                      # Modified: parse webhook CLI args + env vars
└── Consts.MCP.Webhook.cs                     # Added: webhook arg/env constant definitions

McpPlugin.Server.Tests/
└── Webhooks/
    ├── WebhookOptionsTests.cs                # DataArguments parsing tests
    ├── WebhookDispatcherTests.cs             # HTTP dispatch + timeout + failure logging tests
    ├── WebhookEventCollectorTests.cs         # Event collection + dispatch integration tests
    ├── ToolCallEventTests.cs                 # Payload correctness for tool events
    ├── PromptEventTests.cs                   # Payload correctness for prompt events
    ├── ResourceEventTests.cs                 # Payload correctness for resource events
    └── ConnectionEventTests.cs               # Payload correctness for connection events
```

**Structure Decision**: All webhook code is contained within a new `Webhooks/` folder inside `McpPlugin.Server/src/`, following the existing convention of feature-scoped directories (e.g., `Auth/`, `Strategy/`, `Transport/`). Configuration constants are added to `McpPlugin.Common` alongside existing `DataArguments` infrastructure. No new NuGet package is needed.

## Post-Phase 1 Constitution Re-Check

### I. Bridge Architecture — PASS (unchanged)

Design adds outbound HTTP webhooks to `McpPlugin.Server` only. Bridge pattern, SignalR transport, and hub endpoint are untouched. No .NET app subprocess spawning.

### II. Package-First Design — PASS (unchanged)

All new code lives in `McpPlugin.Server`. `McpPlugin.Common` changes are limited to `DataArguments` properties and `Consts` constants — no ASP.NET Core or SignalR dependencies added to Common. No new NuGet package.

### III. Attribute-Based Registration — N/A (unchanged)

No tool/prompt/resource registration changes.

### IV. Test-First Discipline — PASS (unchanged)

Test plan covers: WebhookOptions parsing, WebhookDispatcher HTTP behavior, WebhookEventCollector integration, and per-event-type payload correctness. All will use xUnit + Shouldly + Moq with `await using`. Collection isolation: `[Collection("McpPlugin.Server")]`.

### V. Code Style & Reactive Patterns — PASS (confirmed)

- Namespace `com.IvanMurzak.McpPlugin.Server.Webhooks.*` follows reverse-domain convention
- `Channel<T>` + `BackgroundService` pattern does not conflict with R3 (R3 is for reactive event streams; Channel is for producer-consumer queues — different concerns)
- `IHttpClientFactory` named client follows DI best practices
- `DataArguments` extension follows existing pattern exactly
- All model classes use `sealed class` with `[JsonPropertyName]` attributes — no reflection magic

**Post-Phase 1 Gate: PASSED — no violations. No complexity tracking needed.**

## Complexity Tracking

> No constitution violations detected. Table left empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|--------------------------------------|
| —         | —          | —                                    |
