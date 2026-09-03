# McpPlugin.NullEngine

A **real headless McpPlugin host**: real `McpPluginBuilder`, real SignalR transport, real
attribute-scanned tool / prompt / resource registration — and no editor behind it.

It exists so a test (or an out-of-repo consumer) has ONE deterministic, scriptable engine process to
point an MCP server at. Every tool response is a pure function of its arguments — no clock, no
randomness, no ambient state — so the same call sequence replayed later produces byte-identical
results.

It is **test / fixture infrastructure**: `IsPackable=false`, never packed, never published, and
absent from `deploy.yml` / `release.yml` on purpose.

- Source: `src/Program.cs`, `src/Tools/`, `src/Prompts/`, `src/Resources/`
- In-repo consumer: `McpPlugin.Server.Tests/NullEngineRealTransportTests.cs`

## Build

The project is part of `McpPlugin.sln`, so the repo-root build produces it:

```bash
dotnet build McpPlugin.sln -c Release
```

Output: `McpPlugin.NullEngine/bin/<Configuration>/<tfm>/McpPlugin.NullEngine.dll`
for `<tfm>` in `net8.0`, `net9.0`.

## CLI contract

```
dotnet McpPlugin.NullEngine.dll --mcp-server-endpoint=<url> \
                                [--project-root=<dir>] \
                                [--ready-file=<path>] \
                                [--exit-after-ms=<n>] \
                                [--mcp-server-timeout=<ms>]
dotnet McpPlugin.NullEngine.dll --help
```

| Flag | Meaning | Default |
|---|---|---|
| `--mcp-server-endpoint=<url>` | MCP server base URL. The existing `ConnectionConfig` key; env `MCP_SERVER_ENDPOINT` also works. | `http://localhost:8080` |
| `--project-root=<dir>` | Host project root. Sets `ConnectionConfig.ProjectRootPath`, which anchors the relative `SkillsPath` (`SKILLS`). | a fresh temp directory |
| `--ready-file=<path>` | Write `{pid, tools, prompts, resources, plugin_version}` as JSON once the handshake succeeded. Written via temp-file + rename, so a reader never sees a partial document. | not written |
| `--exit-after-ms=<n>` | Exit `0` this many milliseconds after becoming ready. | run until Ctrl-C |
| `--mcp-server-timeout=<ms>` | Handshake timeout; env `MCP_SERVER_TIMEOUT`. | `10000` |
| `--help` | Print the contract and exit `0` **without connecting**. | — |

`--project-root` is not cosmetic. Without it the plugin cannot resolve the relative default
`SkillsPath` and logs an error during construction (`Cannot resolve relative SkillsPath 'SKILLS'
… ProjectRootPath is not set`, `McpPlugin/src/McpPlugin/McpPlugin.cs`), which the console formatter
writes as a `fail:` line. This host always sets it — that is why a clean startup emits no `fail:`
line at all, and it is the property `NullEngineRealTransportTests` asserts.

> `dotnet run --project McpPlugin.NullEngine` needs an explicit `-f`, because the project multi-targets
> `net8.0;net9.0` (both are needed: `McpPlugin.Server.Tests` runs on both, and each leg locates the
> engine built for its own TFM):
>
> ```bash
> dotnet run --project McpPlugin.NullEngine -f net9.0 -c Release -- --help
> ```

### Ready line

On a successful version handshake the host prints exactly one line to **stdout**:

```
NULL-ENGINE READY tools=12 prompts=2 resources=1 plugin=8.3.0+<commit sha>
```

`plugin=` is the **McpPlugin assembly's** `AssemblyInformationalVersion`, read at runtime from
`typeof(McpPlugin).Assembly` — never the `Consts.PluginVersion` compile-time constant, which cannot
tell a locally built assembly from a released one.

### Exit codes

| Code | Meaning |
|---|---|
| `0` | Clean shutdown: Ctrl-C, `--exit-after-ms` elapsed, or `--help`. |
| `3` | The version handshake did not complete within `--mcp-server-timeout`. A `NULL-ENGINE HANDSHAKE-FAILED …` line is written to stderr. |

### Logging

Console logging is deliberately configured at `Trace`, so the `Microsoft.Extensions.Logging` console
formatter's level prefixes (`trce:` / `dbug:` / `info:` / `warn:` / `fail:` / `crit:`) are observable
by a consumer that captures stdout. Two tools (`fail`, `throw`) are REQUIRED to produce errors, so
they DO emit `fail:` blocks when called; the startup / handshake window is the part that must be
free of them.

## Tools

All names are prefixed `null-engine/`. Arguments below are the JSON `arguments` object of a
`tools/call`. "Response" is the text of the first content block.

| Tool | Arguments | Response | `isError` |
|---|---|---|---|
| `ping` | — | `{"result":"pong"}` | `false` |
| `echo` | `payload: {text: string, number: int, flag: bool}` | `{"result":{"Text":…,"Number":…,"Flag":…}}` — the payload unchanged | `false` |
| `large-payload` | `kb: int` (0..1024, default 1) | `{"result":{"kb":…,"length":…,"payload":"xxx…"}}` | `false` (out of range ⇒ `true`) |
| `fail` | — | `null-engine: deliberate tool failure (fixed message).` | **`true`** |
| `throw` | — | `Tool execution failed for 'Throw': null-engine: deliberate unhandled exception (fixed message).` | **`true`** — and the process stays alive |
| `slow` | `ms: int` (0..10000, default 0) | `{"result":{"slept_ms":…,"status":"slept"}}` | `false` (out of range ⇒ `true`) |
| `progress` | `steps: int` (0..100, default 3) | `{"result":{"emitted":…}}`, plus one `info:` log line per step | `false` (out of range ⇒ `true`) |
| `unicode` | `text: string` (default: a mixed non-ASCII sample) | `{"result":{"text":…}}` — the text unchanged | `false` |
| `nested` | — | `{"result":{"level":1,…{"level":5,…,"child":null}}}` — five levels | `false` |
| `enum-arg` | `color: Red \| Green \| Blue` (default `Red`) | `{"result":{"color":"Green"}}` | `false`; anything else ⇒ `true` |
| `optional-args` | `name: string` (default `world`), `count: int` (default `1`) | `{"result":{"name":"world","count":1}}` | `false` |
| `list-self` | — | `{"result":{"tools":[…]}}` — the names the plugin actually registered, ordered ordinally | `false` |

`progress` reports through log lines rather than MCP progress notifications: the plugin exposes no
progress API on the tool seam today. That is the fallback the contract allows, and it keeps the tool
response itself deterministic.

## Prompts

| Prompt | Arguments | Body |
|---|---|---|
| `null-engine/greeting` | `name: string` (default `world`) | `null-engine: hello, <name>.` |
| `null-engine/static` | — | `null-engine: a fixed prompt body with no arguments.` |

## Resources

| URI | MIME type | Body |
|---|---|---|
| `null-engine://config` | `application/json` | `{"endpoint":…,"project_root":…,"tools":…,"prompts":…,"resources":…,"plugin_version":…}` — the host's own resolved configuration |

## Running it by hand against a local server

`DemoWebApp` is the in-repo `auth=none` Streamable-HTTP server:

```bash
dotnet build McpPlugin.sln -c Release

# terminal 1 - the server. `auth=none` is load-bearing: DataArguments also reads the environment,
# and a worktree's .worktree.env sets MCP_AUTHORIZATION=required.
dotnet DemoWebApp/bin/Release/net9.0/DemoWebApp.dll \
  port=18099 client-transport=streamableHttp auth=none plugin-timeout=10000

# terminal 2 - the engine, once GET http://127.0.0.1:18099/help answers 200
dotnet McpPlugin.NullEngine/bin/Release/net9.0/McpPlugin.NullEngine.dll \
  --mcp-server-endpoint=http://127.0.0.1:18099 \
  --ready-file=/tmp/null-engine-ready.json
```

Then drive a real MCP session against `http://127.0.0.1:18099/mcp`:
`initialize` → capture the `Mcp-Session-Id` response header → `notifications/initialized` →
`tools/list` → `tools/call`. Replies are SSE frames; the JSON is on the `data:` lines.
