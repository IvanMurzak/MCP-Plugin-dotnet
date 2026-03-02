# Feature Specification: Analytics Webhooks for McpPlugin.Server

**Feature Branch**: `001-analytics-webhooks`
**Created**: 2026-03-01
**Status**: Draft
**Input**: User description: "McpPlugin.Server must support a configuration (using launch input arguments or env variables) that will allow to register web hooks. Need to have a proper security, the web hooks must have a header with something like a token. The token should also be configurable at launch. It must be multiple different webhooks to expose analytical information about MCP tool calls, prompts, resources, and connections."

---

## Clarifications

### Session 2026-03-01

- Q: Which duration should "execution duration in milliseconds" measure for tool call webhooks — full round-trip (server receives call → server sends response to AI agent) or plugin execution only (SignalR hop to .NET app and back)? → A: Full round-trip from server receiving the call to server sending the response back to the AI agent.
- Q: What should the HTTP timeout be for each outgoing webhook delivery attempt? → A: Configurable at launch with a 10-second default.
- Q: Should the server enforce HTTPS-only webhook URLs, or allow HTTP URLs? → A: Both HTTP and HTTPS are accepted; the server logs a startup warning for each configured webhook URL that uses HTTP (non-TLS).
- Q: Should webhook payloads include a schema version field to allow consumers to handle future payload changes? → A: Yes — include a `schemaVersion` string field (e.g., `"1.0"`) in every payload.
- Q: What is the maximum acceptable added latency (p99) per tool call when webhooks are enabled under normal load? → A: 10 ms.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Launch-time Webhook Configuration (Priority: P1)

As a server operator, I want to configure webhook endpoints and a security token using launch arguments or environment variables so that no code changes are required to integrate analytics into my existing monitoring infrastructure across different deployment environments.

**Why this priority**: Without the ability to configure webhooks at launch, no other analytics capability is reachable. This is the foundational building block that all other stories depend on.

**Independent Test**: Start the server with a tool webhook URL and token as launch arguments, trigger any tool call, and verify the configured endpoint receives an HTTP POST with the security token in the request header.

**Acceptance Scenarios**:

1. **Given** webhook URLs and a security token are provided as launch arguments, **When** the server starts and a tracked event occurs, **Then** the webhook is delivered to the configured URL with the token in the request header.
2. **Given** webhook URLs and a security token are provided as environment variables, **When** the server starts and a tracked event occurs, **Then** the webhook is delivered identically to when launch arguments are used.
3. **Given** no webhook configuration is provided, **When** the server starts, **Then** the server starts and operates normally with webhook delivery silently disabled — no errors or warnings are raised.
4. **Given** a webhook URL is configured but the security token is omitted, **When** a tracked event occurs, **Then** the webhook is delivered without a token header and the server logs a warning that no token is configured.

---

### User Story 2 - MCP Tool Call Analytics (Priority: P1)

As a system operator, I want to receive webhook notifications for every MCP tool call outcome — both successes and failures — so that I can monitor tool performance, detect error patterns, and measure execution efficiency in my analytics system.

**Why this priority**: Tool calls are the primary interaction in MCP. Visibility into tool success/failure and performance is the most operationally critical analytics category.

**Independent Test**: Configure a tool webhook URL, invoke one tool that succeeds and one that fails, and verify the endpoint receives two separate payloads each containing all required fields with correct values.

**Acceptance Scenarios**:

1. **Given** a tool webhook URL is configured, **When** a tool call completes successfully, **Then** the webhook endpoint receives a payload containing: tool name, request data size in bytes, response data size in bytes, execution status "success", and execution duration in milliseconds.
2. **Given** a tool webhook URL is configured, **When** a tool call fails, **Then** the webhook endpoint receives a payload containing: tool name, request data size in bytes, execution status "failure", execution duration in milliseconds, and failure details (error message or type).
3. **Given** the tool webhook endpoint is unreachable, **When** a tool call completes, **Then** the server continues operating normally, the tool response is returned to the client on time, and the delivery failure is logged.

---

### User Story 3 - Client Connection Lifecycle Events (Priority: P2)

As a system operator, I want to receive webhook notifications when AI agents (MCP clients) and .NET plugin clients (McpPlugin) connect and disconnect so that I can track active sessions, measure session duration, and diagnose connectivity issues.

**Why this priority**: Session-level visibility is the second most important operational concern. Knowing which clients are connected at any moment enables debugging and capacity planning.

**Independent Test**: Configure a connection webhook URL, connect and then disconnect both an AI agent and a McpPlugin client, and verify the endpoint receives four distinct payloads — one for each connect/disconnect event — with correct session IDs and client metadata.

**Acceptance Scenarios**:

1. **Given** a connection webhook URL is configured, **When** an AI agent (MCP client) connects, **Then** the endpoint receives a payload containing: event type "ai-agent-connected", client name, client version, session/connection ID, and any additional metadata available from the MCP protocol handshake.
2. **Given** a connection webhook URL is configured, **When** an AI agent (MCP client) disconnects, **Then** the endpoint receives a payload containing: event type "ai-agent-disconnected" and the session/connection ID.
3. **Given** a connection webhook URL is configured, **When** a McpPlugin (.NET client) connects, **Then** the endpoint receives a payload containing: event type "plugin-connected", all available McpPlugin metadata (name, version, and any registration data), and a session/connection ID.
4. **Given** a connection webhook URL is configured, **When** a McpPlugin (.NET client) disconnects, **Then** the endpoint receives a payload containing: event type "plugin-disconnected" and the session/connection ID.

---

### User Story 4 - MCP Prompt Analytics (Priority: P3)

As a system operator, I want to receive webhook notifications when MCP prompts are retrieved so that I can track which prompts are used most frequently and measure the volume of prompt data served.

**Why this priority**: Prompt analytics completes the observability picture but is less operationally critical than tool calls and connection events.

**Independent Test**: Configure a prompt webhook URL, trigger a prompt retrieval, and verify the endpoint receives a payload with the correct prompt name/ID and data size.

**Acceptance Scenarios**:

1. **Given** a prompt webhook URL is configured, **When** a prompt is retrieved, **Then** the endpoint receives a payload containing: prompt name/ID and the byte size of the prompt content returned.

---

### User Story 5 - MCP Resource Analytics (Priority: P3)

As a system operator, I want to receive webhook notifications when MCP resources are accessed so that I can understand which resources are consumed most frequently and measure data transfer volumes.

**Why this priority**: Resource analytics provides the same secondary observability value as prompt analytics and can be independently enabled.

**Independent Test**: Configure a resource webhook URL, trigger a resource access, and verify the endpoint receives a payload with the correct resource URI and data size.

**Acceptance Scenarios**:

1. **Given** a resource webhook URL is configured, **When** a resource is accessed, **Then** the endpoint receives a payload containing: the resource URI (or URI template identifier if a template was matched) and the byte size of the resource content returned.

---

### Edge Cases

- What happens when the webhook endpoint returns an error response (4xx/5xx)? The server must log the failure with the event type, target URL, and HTTP status, then continue without disrupting MCP client responses.
- What happens when a burst of tool calls occurs and each triggers a webhook? Each delivery must be attempted independently and concurrently without any single delivery blocking others or blocking MCP responses.
- What happens when a tool fails before any response is generated? Response data size should be reported as 0 bytes in the failure payload.
- What happens when request or response payloads are very large? Data size must still be reported as an accurate byte count; no truncation or sampling is acceptable.
- What happens when an AI agent provides no metadata during connection? The payload must still be delivered with session/connection ID; missing metadata fields are omitted rather than causing an error.
- What happens if two clients with the same session ID connect? Each connection lifecycle event is delivered independently based on the actual session ID assigned by the server.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The server MUST support configuring up to four independent webhook endpoints — one each for tool call events, prompt events, resource events, and connection lifecycle events — via launch arguments and/or environment variables.
- **FR-002**: The server MUST support configuring a security token that is sent as a named HTTP header in every outgoing webhook request; both the token value and the header name MUST be configurable at launch.
- **FR-003**: If no webhook URL is configured for an event category, the server MUST operate normally with no webhook delivery and no errors for that category.
- **FR-004**: The server MUST send an HTTP POST to the configured tool webhook URL upon completion of every MCP tool call, regardless of outcome.
- **FR-005**: Tool call webhook payloads MUST include: tool name, request payload size in bytes, response payload size in bytes (0 for failures with no response), execution status ("success" or "failure"), and execution duration in milliseconds measured as the full round-trip from when the server receives the call from the AI agent to when the server sends the response back to the AI agent.
- **FR-006**: Tool call webhook payloads for failed calls MUST additionally include failure details such as the error message or error type.
- **FR-007**: The server MUST send an HTTP POST to the configured connection webhook URL when an AI agent (MCP client) connects, with a payload containing: event type, client name, client version, session/connection ID, and any additional metadata available from the protocol handshake.
- **FR-008**: The server MUST send an HTTP POST to the configured connection webhook URL when an AI agent (MCP client) disconnects, with a payload containing: event type and session/connection ID.
- **FR-009**: The server MUST send an HTTP POST to the configured connection webhook URL when a McpPlugin (.NET client) connects, with a payload containing: event type, all available McpPlugin registration metadata, and a session/connection ID.
- **FR-010**: The server MUST send an HTTP POST to the configured connection webhook URL when a McpPlugin (.NET client) disconnects, with a payload containing: event type and session/connection ID.
- **FR-011**: The server MUST send an HTTP POST to the configured prompt webhook URL when any prompt is retrieved, with a payload containing: prompt name/ID and response byte size.
- **FR-012**: The server MUST send an HTTP POST to the configured resource webhook URL when any resource is accessed, with a payload containing: resource URI or matched URI template identifier, and response byte size.
- **FR-013**: All webhook delivery MUST be asynchronous and non-blocking — no webhook operation may add measurable latency to MCP client responses.
- **FR-014**: Webhook delivery failures (network errors, DNS failures, HTTP error responses) MUST be logged with: event type, target URL, and failure reason. Security tokens MUST NOT appear in logs.
- **FR-015**: Each webhook payload MUST include a timestamp (UTC), an event type discriminator, and a `schemaVersion` string (initial value `"1.0"`) to allow consumers to identify, order, and evolve their parsing logic across future payload changes.
- **FR-016**: The HTTP timeout for each outgoing webhook request MUST be configurable at launch (via launch argument or environment variable) with a default of 10 seconds. Requests exceeding the timeout MUST be cancelled and treated as delivery failures per FR-014.
- **FR-017**: Both HTTP and HTTPS webhook URLs MUST be accepted. At server startup, if any configured webhook URL uses HTTP (non-TLS), the server MUST log a warning indicating that the security token will be transmitted without transport-layer encryption.

### Key Entities

- **WebhookEndpointConfig**: The configuration for a single event category's webhook — URL, security token value, and security header name. A global HTTP timeout (default 10 s, configurable at launch) applies to all endpoints.
- **ToolCallEvent**: An analytics record for a completed tool call, containing tool name, request and response byte sizes, execution status, full round-trip duration in milliseconds (server-receive to server-send), and optional failure details.
- **PromptEvent**: An analytics record for a prompt retrieval, containing prompt name/ID and response byte size.
- **ResourceEvent**: An analytics record for a resource access, containing the resource URI or matched URI template identifier and response byte size.
- **ConnectionEvent**: An analytics record for a client lifecycle transition, containing event type (connected/disconnected), client type (AI agent or McpPlugin), session/connection ID, and available client metadata.
- **WebhookPayload**: The JSON body sent in every outgoing HTTP POST, wrapping an event record with a UTC timestamp, event type discriminator, and `schemaVersion` string (initial value `"1.0"`).

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All four webhook event categories (tools, prompts, resources, connections) can be independently enabled and disabled at launch without code changes.
- **SC-002**: Webhook delivery adds no more than 10 ms (p99) to tool call round-trip time compared to equivalent load with webhooks disabled.
- **SC-003**: 100% of completed tool calls (success and failure) with a configured tool webhook URL result in exactly one webhook delivery attempt per call.
- **SC-004**: All four connection lifecycle event types (AI agent connected, AI agent disconnected, McpPlugin connected, McpPlugin disconnected) produce correctly formed webhook payloads when a connection webhook is configured.
- **SC-005**: The server starts and operates correctly when zero, one, two, three, or four webhook categories are configured simultaneously.
- **SC-006**: Failed webhook deliveries are logged with sufficient diagnostic information (event type, URL, failure reason) for operator investigation, without ever exposing the security token in any log output.
- **SC-007**: Security token configuration via environment variable produces identical behavior to configuration via launch argument.

---

## Assumptions

- **Single shared security token**: One token and one header name are configured globally and applied to all active webhook endpoints. If different endpoints require different tokens, operators can run separate server instances. This assumption should be revisited if per-endpoint token isolation becomes a hard requirement.
- **Fire-and-forget delivery**: Webhook delivery is attempted once per event with no automatic retry on failure. This is standard for analytics webhooks where occasional data loss is acceptable. If guaranteed delivery is needed, the consuming service should implement idempotent ingest and operators should use a durable queue in front of the webhook endpoint.
- **JSON payloads**: All webhook payloads are delivered as UTF-8 encoded JSON. No other payload format is considered.
- **Data size definition**: "Data size" refers to the byte length of the serialized payload (request or response body) as transmitted on the wire, before any compression.
- **Resource URI field**: The term "resource path pattern" in the user description refers to the MCP resource URI — specifically the URI of the resource accessed, or the URI template identifier if the access matched a registered template rather than an exact URI.
- **Connection metadata availability**: AI agent metadata (name, version) is sourced from the MCP `initialize` request `clientInfo` field. McpPlugin metadata is whatever the plugin sends during its SignalR handshake. Fields not provided by the client are omitted from the webhook payload without error.
