# Tasks: Analytics Webhooks for McpPlugin.Server

**Input**: Design documents from `/specs/001-analytics-webhooks/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/webhook-api.md, quickstart.md

**Tests**: Included per plan.md constitution check (Red-Green-Refactor discipline).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create directory structure for webhook subsystem in McpPlugin.Server

- [ ] T001 Create Webhooks directory structure (Config/, Models/, Services/, Extensions/) under McpPlugin.Server/src/Webhooks/
- [ ] T002 [P] Create Webhooks/ test directory under McpPlugin.Server.Tests/

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared event models, payload envelope, internal queue record, and service interfaces that ALL user stories depend on

**CRITICAL**: No user story work can begin until this phase is complete

- [ ] T003 [P] Create WebhookPayload&lt;T&gt; generic envelope model (schemaVersion, eventType, timestamp, data) in McpPlugin.Server/src/Webhooks/Models/WebhookPayload.cs
- [ ] T004 [P] Create ToolCallEvent sealed class (toolName, requestSizeBytes, responseSizeBytes, status, durationMs, errorDetails) with [JsonPropertyName] attributes in McpPlugin.Server/src/Webhooks/Models/ToolCallEvent.cs
- [ ] T005 [P] Create PromptEvent sealed class (promptName, responseSizeBytes) with [JsonPropertyName] attributes in McpPlugin.Server/src/Webhooks/Models/PromptEvent.cs
- [ ] T006 [P] Create ResourceEvent sealed class (resourceUri, responseSizeBytes) with [JsonPropertyName] attributes in McpPlugin.Server/src/Webhooks/Models/ResourceEvent.cs
- [ ] T007 [P] Create ConnectionEvent sealed class (eventType, clientType, sessionId, clientName?, clientVersion?, metadata?) with [JsonPropertyName] attributes in McpPlugin.Server/src/Webhooks/Models/ConnectionEvent.cs
- [ ] T008 [P] Create WebhookMessage sealed record (targetUrl, jsonPayload, headerName?, tokenValue?) in McpPlugin.Server/src/Webhooks/Services/WebhookMessage.cs
- [ ] T009 [P] Create IWebhookDispatcher interface (EnqueueAsync method accepting WebhookMessage) in McpPlugin.Server/src/Webhooks/Services/IWebhookDispatcher.cs
- [ ] T010 [P] Create IWebhookEventCollector interface (OnToolCall, OnPromptRetrieved, OnResourceAccessed, OnAiAgentConnected, OnAiAgentDisconnected, OnPluginConnected, OnPluginDisconnected methods) in McpPlugin.Server/src/Webhooks/Services/IWebhookEventCollector.cs

**Checkpoint**: Foundation ready — all shared types and interfaces defined. User story implementation can now begin.

---

## Phase 3: User Story 1 — Launch-time Webhook Configuration (Priority: P1) MVP

**Goal**: Configure webhook endpoints and security token using launch arguments or environment variables, with fire-and-forget async dispatch infrastructure.

**Independent Test**: Start the server with a tool webhook URL and token as launch arguments, trigger any tool call, and verify the configured endpoint receives an HTTP POST with the security token in the request header.

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T011 [P] [US1] Write WebhookOptions parsing tests (CLI args, env vars, defaults, validation, computed properties) in McpPlugin.Server.Tests/Webhooks/WebhookOptionsTests.cs
- [ ] T012 [P] [US1] Write WebhookDispatcher tests (HTTP POST delivery, token header, timeout cancellation, failure logging, no-op when disabled) using MockHttpMessageHandler in McpPlugin.Server.Tests/Webhooks/WebhookDispatcherTests.cs
- [ ] T013 [P] [US1] Write WebhookEventCollector tests (event serialization, envelope fields, channel enqueue, no-op when category disabled) in McpPlugin.Server.Tests/Webhooks/WebhookEventCollectorTests.cs

### Implementation for User Story 1

- [ ] T014 [P] [US1] Add webhook argument constants (WebhookToolUrl, WebhookPromptUrl, WebhookResourceUrl, WebhookConnectionUrl, WebhookToken, WebhookHeader, WebhookTimeout) to Consts.MCP.Server.Args and Consts.MCP.Server.Env in McpPlugin.Common/src/Utils/Consts.MCP.cs
- [ ] T015 [P] [US1] Create WebhookOptions sealed class with URL properties, TokenValue, HeaderName, TimeoutMs, computed IsEnabled/IsToolEnabled/etc, and static factory method parsing from IDataArguments in McpPlugin.Server/src/Webhooks/Config/WebhookOptions.cs
- [ ] T016 [US1] Extend DataArguments to parse webhook CLI args and env vars (env parsed first, CLI overrides) following existing pattern in McpPlugin.Common/src/Utils/DataArguments.cs
- [ ] T017 [US1] Implement WebhookDispatcher as BackgroundService with Channel&lt;WebhookMessage&gt; (bounded 1024, DropOldest, SingleReader), IHttpClientFactory named client "webhook", configurable timeout, token header injection, failure logging (no token in logs) in McpPlugin.Server/src/Webhooks/Services/WebhookDispatcher.cs
- [ ] T018 [US1] Implement WebhookEventCollector (accepts domain event params, creates event models, serializes to JSON with camelCase + WhenWritingNull, wraps in WebhookPayload envelope, enqueues WebhookMessage to dispatcher) in McpPlugin.Server/src/Webhooks/Services/WebhookEventCollector.cs
- [ ] T019 [US1] Create WebhookServiceExtensions.AddWebhooks(IServiceCollection, IDataArguments) that registers WebhookOptions, named HttpClient with SocketsHttpHandler (ConnectTimeout 2s), WebhookDispatcher as hosted service, WebhookEventCollector as singleton; skip all registration if no URLs configured (register no-op IWebhookEventCollector) in McpPlugin.Server/src/Webhooks/Extensions/WebhookServiceExtensions.cs
- [ ] T020 [US1] Wire AddWebhooks(dataArguments) call into WithMcpPluginServer extension method in McpPlugin.Server/src/Extension/ExtensionsMcpServerBuilder.cs
- [ ] T021 [US1] Add startup warnings: log warning for each HTTP (non-TLS) webhook URL, log warning when webhook URLs configured but no token set, in WebhookServiceExtensions.cs or WebhookDispatcher.StartAsync

**Checkpoint**: US1 complete — server accepts webhook configuration via CLI args and env vars, dispatches fire-and-forget HTTP POSTs with security token. No event emission yet (covered by US2-US5).

---

## Phase 4: User Story 2 — MCP Tool Call Analytics (Priority: P1)

**Goal**: Emit webhook notifications for every MCP tool call (success and failure) with tool name, request/response sizes, duration, status, and error details.

**Independent Test**: Configure a tool webhook URL, invoke one tool that succeeds and one that fails, verify endpoint receives two payloads with all required fields.

### Tests for User Story 2

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T022 [P] [US2] Write ToolCallEvent payload tests (correct JSON shape for success/failure, zero responseSizeBytes on failure, accurate duration, errorDetails presence) in McpPlugin.Server.Tests/Webhooks/ToolCallEventTests.cs

### Implementation for User Story 2

- [ ] T023 [US2] Wrap CallToolHandler in ExtensionsMcpServer.cs: add Stopwatch timing around ToolRouter.Call, measure request size (serialize request.Params.Arguments), measure response size (serialize CallToolResult), resolve IWebhookEventCollector from request.Services, call OnToolCall with all fields in McpPlugin.Server/src/Extension/ExtensionsMcpServer.cs
- [ ] T024 [US2] Handle tool call failure path: set status "failure", responseSizeBytes 0 when no response body, extract error message from result.Content for errorDetails field in McpPlugin.Server/src/Extension/ExtensionsMcpServer.cs

**Checkpoint**: US2 complete — every tool call (success/failure) fires a webhook with full analytics payload. Can be tested independently with US1 configuration.

---

## Phase 5: User Story 3 — Client Connection Lifecycle Events (Priority: P2)

**Goal**: Emit webhook notifications when AI agents and McpPlugin clients connect and disconnect, with session IDs and client metadata.

**Independent Test**: Configure a connection webhook URL, connect and disconnect both an AI agent and a McpPlugin client, verify four distinct payloads with correct event types and session IDs.

### Tests for User Story 3

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T025 [P] [US3] Write ConnectionEvent payload tests (all four event types: ai-agent-connected, ai-agent-disconnected, plugin-connected, plugin-disconnected; correct sessionId, metadata inclusion, metadata omission when unavailable) in McpPlugin.Server.Tests/Webhooks/ConnectionEventTests.cs

### Implementation for User Story 3

- [ ] T026 [US3] Inject IWebhookEventCollector into McpServerHub constructor and emit plugin-connected event (with Context.ConnectionId and available metadata) in OnConnectedAsync, plugin-disconnected event in OnDisconnectedAsync in McpPlugin.Server/src/Hub/McpServerHub.cs
- [ ] T027 [US3] Inject IWebhookEventCollector into McpServerService and emit ai-agent-connected event (with McpServer.SessionId, ClientInfo.Name, ClientInfo.Version, additional metadata from ClientInfo) after MCP initialize handshake in McpPlugin.Server/src/McpServerService.cs
- [ ] T028 [US3] Emit ai-agent-disconnected event (with session ID) from McpServerService on session end/StopAsync in McpPlugin.Server/src/McpServerService.cs

**Checkpoint**: US3 complete — all four connection lifecycle events fire correct webhooks. Can be tested independently with US1 configuration.

---

## Phase 6: User Story 4 — MCP Prompt Analytics (Priority: P3)

**Goal**: Emit webhook notifications when MCP prompts are retrieved, with prompt name and response data size.

**Independent Test**: Configure a prompt webhook URL, trigger a prompt retrieval, verify payload contains correct prompt name and response byte size.

### Tests for User Story 4

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T029 [P] [US4] Write PromptEvent payload tests (correct JSON shape, promptName field, responseSizeBytes calculation) in McpPlugin.Server.Tests/Webhooks/PromptEventTests.cs

### Implementation for User Story 4

- [ ] T030 [US4] Wrap GetPromptHandler in ExtensionsMcpServer.cs: call PromptRouter.Get, measure response size (serialize GetPromptResult), resolve IWebhookEventCollector from request.Services, call OnPromptRetrieved with prompt name and response byte size in McpPlugin.Server/src/Extension/ExtensionsMcpServer.cs

**Checkpoint**: US4 complete — prompt retrieval fires webhooks. Can be tested independently with US1 configuration.

---

## Phase 7: User Story 5 — MCP Resource Analytics (Priority: P3)

**Goal**: Emit webhook notifications when MCP resources are accessed, with resource URI and response data size.

**Independent Test**: Configure a resource webhook URL, trigger a resource access, verify payload contains correct resource URI and response byte size.

### Tests for User Story 5

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T031 [P] [US5] Write ResourceEvent payload tests (correct JSON shape, resourceUri field including URI template matching, responseSizeBytes calculation) in McpPlugin.Server.Tests/Webhooks/ResourceEventTests.cs

### Implementation for User Story 5

- [ ] T032 [US5] Wrap ReadResourceHandler in ExtensionsMcpServer.cs: call ResourceRouter.ReadResource, measure response size (serialize ReadResourceResult), resolve IWebhookEventCollector from request.Services, call OnResourceAccessed with resource URI and response byte size in McpPlugin.Server/src/Extension/ExtensionsMcpServer.cs

**Checkpoint**: US5 complete — resource access fires webhooks. Can be tested independently with US1 configuration.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Validation, build verification, and overall quality checks

- [ ] T033 [P] Verify zero-overhead when no webhook URLs configured: confirm no BackgroundService, no HttpClient, no-op collector registered
- [ ] T034 [P] Verify security token never appears in log output across all logging paths
- [ ] T035 Run full test suite on net8.0 and net9.0 with dotnet test
- [ ] T036 Run build verification with dotnet build McpPlugin.sln
- [ ] T037 Run quickstart.md validation scenarios (minimal, all-categories, env-var-only configurations)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup (directory structure exists) — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Foundational — BLOCKS US2, US3, US4, US5 (provides dispatch infrastructure)
- **US2 (Phase 4)**: Depends on US1 only — independent of US3, US4, US5
- **US3 (Phase 5)**: Depends on US1 only — independent of US2, US4, US5
- **US4 (Phase 6)**: Depends on US1 only — independent of US2, US3, US5
- **US5 (Phase 7)**: Depends on US1 only — independent of US2, US3, US4
- **Polish (Phase 8)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Foundational → US1 (required for all others)
- **User Story 2 (P1)**: US1 → US2 (no dependencies on US3/US4/US5)
- **User Story 3 (P2)**: US1 → US3 (no dependencies on US2/US4/US5)
- **User Story 4 (P3)**: US1 → US4 (no dependencies on US2/US3/US5)
- **User Story 5 (P3)**: US1 → US5 (no dependencies on US2/US3/US4)

### Within Each User Story

- Tests MUST be written and FAIL before implementation
- Models/interfaces before service implementations
- Service implementations before DI wiring
- DI wiring before integration points (router wrappers, hub injection)
- Story complete before moving to next priority

### Parallel Opportunities

- **Phase 2**: All 8 model/interface tasks (T003-T010) can run in parallel
- **Phase 3**: Test tasks T011-T013 can run in parallel; T014-T015 can run in parallel
- **After US1 completes**: US2, US3, US4, US5 can ALL proceed in parallel (different files, independent concerns)
- **Phase 4+5+6+7**: If parallelized, all four user stories can be implemented simultaneously by different developers

---

## Parallel Example: After US1 Completion

```
# All four event stories can launch simultaneously:
Developer A: US2 — Tool call wrapper in ExtensionsMcpServer.cs (T022-T024)
Developer B: US3 — Connection events in McpServerHub.cs + McpServerService.cs (T025-T028)
Developer C: US4 — Prompt wrapper in ExtensionsMcpServer.cs (T029-T030)
Developer D: US5 — Resource wrapper in ExtensionsMcpServer.cs (T031-T032)

# Note: US2, US4, US5 all modify ExtensionsMcpServer.cs — if done by one developer,
# execute sequentially. If parallelized across developers, merge carefully.
```

---

## Parallel Example: Phase 2 (Foundational)

```
# All model and interface files are independent — launch all 8 in parallel:
Task T003: WebhookPayload<T> in Models/WebhookPayload.cs
Task T004: ToolCallEvent in Models/ToolCallEvent.cs
Task T005: PromptEvent in Models/PromptEvent.cs
Task T006: ResourceEvent in Models/ResourceEvent.cs
Task T007: ConnectionEvent in Models/ConnectionEvent.cs
Task T008: WebhookMessage in Services/WebhookMessage.cs
Task T009: IWebhookDispatcher in Services/IWebhookDispatcher.cs
Task T010: IWebhookEventCollector in Services/IWebhookEventCollector.cs
```

---

## Implementation Strategy

### MVP First (User Story 1 + User Story 2)

1. Complete Phase 1: Setup (T001-T002)
2. Complete Phase 2: Foundational (T003-T010)
3. Complete Phase 3: US1 — Webhook Configuration & Dispatch (T011-T021)
4. **STOP and VALIDATE**: Verify dispatcher works with a test webhook endpoint
5. Complete Phase 4: US2 — Tool Call Analytics (T022-T024)
6. **STOP and VALIDATE**: Trigger tool calls, verify webhook delivery
7. Deploy/demo if ready — tool call analytics alone provides significant operational value

### Incremental Delivery

1. Setup + Foundational + US1 → Configuration and dispatch infrastructure ready
2. Add US2 (Tool Call Analytics) → Test independently → **MVP deployed**
3. Add US3 (Connection Events) → Test independently → Session visibility added
4. Add US4 (Prompt Analytics) → Test independently → Prompt tracking added
5. Add US5 (Resource Analytics) → Test independently → Full observability suite
6. Polish phase → Production readiness

### Suggested MVP Scope

**US1 + US2** — Webhook configuration and tool call analytics cover the most operationally critical use case. Connection events (US3) is the natural next increment.

---

## File Summary

### New Files (15)

| File | Phase | Story |
|---|---|---|
| McpPlugin.Server/src/Webhooks/Config/WebhookOptions.cs | 3 | US1 |
| McpPlugin.Server/src/Webhooks/Models/WebhookPayload.cs | 2 | — |
| McpPlugin.Server/src/Webhooks/Models/ToolCallEvent.cs | 2 | — |
| McpPlugin.Server/src/Webhooks/Models/PromptEvent.cs | 2 | — |
| McpPlugin.Server/src/Webhooks/Models/ResourceEvent.cs | 2 | — |
| McpPlugin.Server/src/Webhooks/Models/ConnectionEvent.cs | 2 | — |
| McpPlugin.Server/src/Webhooks/Services/WebhookMessage.cs | 2 | — |
| McpPlugin.Server/src/Webhooks/Services/IWebhookDispatcher.cs | 2 | — |
| McpPlugin.Server/src/Webhooks/Services/IWebhookEventCollector.cs | 2 | — |
| McpPlugin.Server/src/Webhooks/Services/WebhookDispatcher.cs | 3 | US1 |
| McpPlugin.Server/src/Webhooks/Services/WebhookEventCollector.cs | 3 | US1 |
| McpPlugin.Server/src/Webhooks/Extensions/WebhookServiceExtensions.cs | 3 | US1 |
| McpPlugin.Server.Tests/Webhooks/WebhookOptionsTests.cs | 3 | US1 |
| McpPlugin.Server.Tests/Webhooks/WebhookDispatcherTests.cs | 3 | US1 |
| McpPlugin.Server.Tests/Webhooks/WebhookEventCollectorTests.cs | 3 | US1 |

### New Test Files (4 additional)

| File | Phase | Story |
|---|---|---|
| McpPlugin.Server.Tests/Webhooks/ToolCallEventTests.cs | 4 | US2 |
| McpPlugin.Server.Tests/Webhooks/ConnectionEventTests.cs | 5 | US3 |
| McpPlugin.Server.Tests/Webhooks/PromptEventTests.cs | 6 | US4 |
| McpPlugin.Server.Tests/Webhooks/ResourceEventTests.cs | 7 | US5 |

### Modified Files (5)

| File | Phase | Story |
|---|---|---|
| McpPlugin.Common/src/Utils/Consts.MCP.cs | 3 | US1 |
| McpPlugin.Common/src/Utils/DataArguments.cs | 3 | US1 |
| McpPlugin.Server/src/Extension/ExtensionsMcpServerBuilder.cs | 3 | US1 |
| McpPlugin.Server/src/Extension/ExtensionsMcpServer.cs | 4-7 | US2, US4, US5 |
| McpPlugin.Server/src/Hub/McpServerHub.cs | 5 | US3 |
| McpPlugin.Server/src/McpServerService.cs | 5 | US3 |

---

## Notes

- [P] tasks = different files, no dependencies — safe to run in parallel
- [Story] label maps task to specific user story for traceability
- All .cs files MUST include the ASCII art Apache-2.0 license header (copy from any existing file)
- All classes use namespace `com.IvanMurzak.McpPlugin.Server.Webhooks.*` (or `com.IvanMurzak.McpPlugin.Common.*` for Common changes)
- Allman braces, `_camelCase` private fields, `PascalCase` public members
- System.Text.Json with `[JsonPropertyName]` for all model classes — explicit camelCase mapping
- Security token MUST NEVER appear in log output
- Channel bounded capacity: 1024, FullMode: DropOldest, SingleReader: true
- HTTP timeout default: 10000ms, SocketsHttpHandler.ConnectTimeout: 2s
- Commit after each task or logical group
