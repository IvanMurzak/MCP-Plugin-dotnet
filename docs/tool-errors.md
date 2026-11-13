# Tool errors and HTTP status

How a failing `[AiTool]` method is reported, and which plugin and server versions get which behavior.

## The MCP channel

On `/mcp` (and stdio) every tool error is an ordinary `tools/call` result with `isError: true` and the
tool's message. The table below only changes the HTTP status of the direct REST endpoint
`POST /api/tools/{name}`.

## Status table (`POST /api/tools/{name}`)

| The tool... | Kind | HTTP |
| --- | --- | --- |
| returns `ResponseCallTool.Error(message)` (or `Unprocessable(message)`) | `BadRequest` | 400 (422) |
| receives arguments that cannot be bound (ReflectorNet throws `ArgumentException`; `JsonException` on the named-argument path) | `BadRequest` | 400 |
| **throws** a fault type: `NullReferenceException`, `IndexOutOfRangeException`, `InvalidCastException`, `NotImplementedException`, `ObjectDisposedException`, `OutOfMemoryException`, `TypeLoadException`, `MissingMemberException`, `TypeInitializationException`, `FileLoadException`, `BadImageFormatException`, `InvalidProgramException`, `TimeoutException`, `OperationCanceledException` (and subclasses) | `Internal` | 500 |
| **throws** `ToolUnavailableException(message)`: the host cannot serve the call right now (starting, compiling, reloading) | `Unavailable` | 503 |
| **throws** `ToolUnavailableException(message, notExecuted: true)`: the same, and the tool did nothing yet | `Unavailable` (`ResponseCallTool.NotExecuted`) | 503 |
| **throws** anything else: `Exception`, `InvalidOperationException`, `NotSupportedException`, your own exception types | `BadRequest` | 422 |
| succeeds, but its result cannot be serialized | `Internal` | 500 |

A hand-written `IRunTool` / `ProxyTool` that throws keeps the managers' own mapping: `ArgumentException` 400,
`KeyNotFoundException` 404, `InvalidOperationException` 409, `TimeoutException` 504, anything else 500.

## The rule for tool authors

- **Throwing means "I could not do what was asked"** (invalid code to compile, an object that does not exist,
  an operation the target cannot do). That is the caller's problem, so it is 422.
- **500 is kept for fault types** that point at a bug in the tool or a broken plugin. Never let a
  `NullReferenceException` escape for missing input: throw a plain or custom exception with a message the agent
  can act on, or return `ResponseCallTool.Error(message)`.
- The message is the same for 422 and 500: `Tool execution failed for '<tool>': <message>`, where `<message>`
  is the thrown exception's message (for an `async` tool whose exception carries an inner exception, the inner
  one's).
- Edge cases: an `ArgumentException` thrown directly by an `async` tool is reported like a binding failure
  (400); anything else thrown while the tool's instance is created or its arguments are converted counts as
  thrown by the tool.

The rule lives in `RunTool.IsFrameworkFault` and `RunTool.ExecutionFailed`.

## `ToolUnavailableException`

Use it for a failure that is nobody's fault in the request: the editor is compiling, a service the tool needs
is not built yet.

- Pass `notExecuted: true` only when the tool changed nothing before it threw, and only for a state a plugin
  reload ends (a pending script reload). A server with the call guard (McpPlugin.Server 8.14.0+, for plugins on
  8.14.0+) then sends the call again, after the plugin reconnects or every couple of seconds while it stays
  connected, within `plugin-reconnect-wait-seconds`. When that window ends it answers 503 with a
  `[plugin_reloading]` error that ends with the tool's message.
- Without `notExecuted` the kind carries no status, so no server sends the call again (it may have done part of
  its work) and the tool's message comes back as is.
- Keep the phrase "this call was not run" out of the message: the call guard reads it as a reload-gate refusal.
- A hand-written `IRunTool` that throws it gets the same kind and status.

## Versions

- **8.15.0**: the 422 rule. It travels as the existing `BadRequest` kind plus `HttpStatusCode = 422`, which
  every server since McpPlugin 7.0.0 honours, so a new plugin on an older server also gets 422. A plugin before
  8.15.0 sends `Internal` for every thrown exception, which stays 500 on any server.
- **8.16.0**: `ToolUnavailableException`. Every server since McpPlugin 7.0.0 maps `Unavailable` to 503.
- **8.14.0**: tool-call argument checks and the call guard. A plugin on McpPlugin 8.14.0 or newer (it reports
  `mcpPluginVersion` in its handshake) gets server-side tool-argument validation (required arguments and types)
  and the call guard (one whole-call deadline, reload and re-send handling). An older plugin, or one that
  reports no version, has its calls forwarded as server 9.2.x forwarded them: no argument validation, no
  whole-call deadline, no rewriting of its errors. Only a call carrying a lone UTF-16 surrogate, which cannot be
  delivered to any plugin, is refused with a clear error.
- Connections from older plugins keep the 9.2.x newest-wins replacement: the editor and runtime roles apply
  only when both connections are 8.14+ plugins. An old plugin's connection replaces the previous one at its
  version handshake (milliseconds after connecting), not at connect. The `mcpPluginVersion` field is optional on
  the wire: a server before 8.14.0 ignores it.
