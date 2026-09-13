# T2 chain fixtures — record / replay contract, schema 1

A **chain fixture** is what an engine's MCP surface looked like at one commit: the tool catalogue it
published and the answers a fixed battery of calls received. Engine legs re-record it on every run
and **diff** it against the committed copy; a diff **is** a contract change, reviewed in the engine
repository's own pull request. The same fixture is then **replayed** by
[`McpPlugin.NullEngine`](../McpPlugin.NullEngine/README.md) so consumers (the desktop app, the cloud
server) can be tested against a real engine's contract with no engine installed.

Two implementations exist and must stay byte-identical:

| | where | used by |
|---|---|---|
| `scripts/chain_fixture.py` | this repository, **vendored byte-identically** into each engine repo as `.github/scripts/chain_fixture.py` | recording, canonicalising, diffing in CI |
| `McpPlugin.NullEngine/src/Replay/FixtureLoader.cs` | this repository | `--replay` inside the plugin process |

Neither is generated from the other. `McpPlugin.Tests/Chain/CanonicalizeParityTests.cs` runs both
over the same inputs and compares bytes; that comparison is the only thing keeping them in step, so
**a change to one is a change to both**.

Fixtures live in the **engine's own repository** under
`tests/chain-fixtures/<engine-version>/tools.jsonl` — the repository whose contract it is, so a diff
shows up in that repository's pull request. This repository commits exactly one, as the format's
reference sample: [`tests/chain-fixtures/null-engine/tools.jsonl`](../tests/chain-fixtures/null-engine/tools.jsonl).

---

## F1 — the file

`tools.jsonl`: **UTF-8, LF, one JSON object per line**, no trailing blank line beyond the final LF.
(`.gitattributes` pins `*.jsonl` to `eol=lf`; this repository has `core.autocrlf` on, and a CRLF
checkout would break every byte comparison on Windows.)

Both readers **tolerate on input** what neither ever writes: a trailing CR on a line, and a leading
UTF-8 BOM. Tolerated identically on both sides on purpose — `File.ReadAllText(path, Encoding.UTF8)`
strips a BOM by itself, so before that was matched in Python the replay host served a BOM'd fixture
happily while the diff that gates CI refused the same file. A disagreement about which files are
*accepted* is as much a defect here as a disagreement about their canonical bytes.

**Line 1 — `meta`:**

```json
{"schema":1,"kind":"meta","engine":"unity","engine_version":"6000.0.23f1","surface":"editor",
 "plugin_version":"0.24.1","mcp_plugin_version":"8.3.0+e647f34","recorded_at":"2026-09-04T10:11:12Z",
 "recorder":"mcp-client"}
```

| field | meaning |
|---|---|
| `schema` | always `1`. Anything else is a **schema mismatch**, exit 4 — the two files are not comparable, so it is never reported as a contract change. |
| `engine` | `unity` \| `godot` \| `unreal` \| `null-engine` |
| `engine_version` | the engine build the fixture was taken against |
| `surface` | `editor` \| `runtime` |
| `plugin_version` | the ENGINE PLUGIN's version (masked — see F3) |
| `mcp_plugin_version` | the `McpPlugin` assembly's informational version (masked) |
| `recorded_at` | ISO-8601 UTC, seconds precision (masked) |
| `recorder` | `in-process` \| `mcp-client` — see *Two recorders* below |

**Then one `tool` line per `tools/list` entry**, carrying the
[`ResponseListTool`](../McpPlugin.Common/src/Data/Response/Tool/List/ResponseListTool.cs) fields that
reach an MCP client:

```json
{"kind":"tool","name":"null-engine/ping","title":"Ping","description":"…",
 "inputSchema":{…},"outputSchema":{…},
 "readOnlyHint":true,"destructiveHint":false,"idempotentHint":true,"openWorldHint":false}
```

A field the tool does not declare is **omitted**, never written as `null`.

Deliberately **not** recorded: `Enabled` (an untrusted client is served only enabled tools, so an
MCP capture could never observe it) and the skill metadata `_meta.skillDescription` /
`_meta.skillBody` (behind the `X-McpPlugin-Skill-Meta: 1` opt-in). Recording either would make the
two recorder kinds disagree for a reason that is not a contract change.

**Then one `call` line per battery entry:**

```json
{"kind":"call","name":"null-engine/ping","args":{},
 "args_hash":"sha256:44136fa3…",
 "response":{"status":"success","content":[{"type":"text","text":"{\"result\":\"pong\"}"}],
             "structuredContent":{"result":"pong"},"errorKind":"None"}}
```

* `args` — the arguments as sent, verbatim from the battery.
* `args_hash` — `"sha256:" + sha256(canonical JSON of the arguments)`, **always recomputed** by
  `canonicalize`, never trusted from the input: it is a derived field, and a stale one would break
  replay lookup silently.

  **Numbers are hashed as their raw token text.** Measured: the SignalR payload serializer converts
  every JSON number to a string on the way to the plugin — a client sending `{"kb":1}` reaches the
  tool as `{"kb":"1"}`, recursively (`{"payload":{"number":7,"flag":true}}` arrives as
  `{"payload":{"number":"7","flag":true}}`; booleans and strings keep their kind). A hash over the
  client's view could therefore never match one taken inside the plugin, and replay would miss every
  call carrying a number. So `1` and `"1"` hash the same — exactly the identification the transport
  already makes. Avoid exotic number literals in a battery (`+1`, `1.0` vs `1`, exponents): how the
  transport re-renders those is not part of this contract.
* `response` — the **MCP-observable** envelope:

  | field | notes |
  |---|---|
  | `status` | `success` \| `error` (from `isError`) |
  | `content[]` | blocks reduced to `type`, `text`, `data`, `mimeType`, `resource`; anything else an SDK serialises is dropped, so a transport-library upgrade is not a contract change. A **text** block carries no `mimeType` — the server drops it (`ExtensionsContentBlock.ToContent`). |
  | `structuredContent` | as served; omitted when absent |
  | `errorKind` | **recorder-scoped, see below**; omitted when the recorder cannot observe it |

### The battery

A battery is a JSON document listing the calls to make:

```json
{"schema": 1,
 "calls": [ {"name": "null-engine/ping", "args": {}},
            {"name": "null-engine/echo", "args": {"payload": {"text": "x", "number": 7}}} ]}
```

Call the same tool twice with different arguments where the arguments matter — that is the only
thing that makes the `(name, args_hash)` key of F6 observable at all.

### Two recorders

| | `in-process` | `mcp-client` |
|---|---|---|
| how | `IToolManager.RunListTool` + `RunCallTool` inside the engine process | JSON-RPC over the server's Streamable-HTTP `/mcp` |
| here | `McpPlugin.NullEngine --dump-raw <out> --battery <file>` | `chain_fixture.py record --server-url <base> …` |
| needs | nothing but the plugin | a running server with the plugin connected |
| sees `errorKind` | **yes** | **no** |

`errorKind` never reaches an MCP client: the server maps a plugin response onto
`CallToolResult{IsError, Content, StructuredContent}` and drops the kind
(`McpPlugin.Server/src/Extension/ExtensionsTool.cs`). That is the one field whose presence is decided
by *how* the fixture was captured rather than by the contract, which is what `--cross-recorder`
exists for.

An in-process dump records the **MCP-observable projection** of each response — it mirrors
`ToolRouter.Call` (an error discards the response's content and rebuilds one text block from the
packed message), `ExtensionsTool.ToCallToolResult` and `ExtensionsContentBlock.ToContent`. That
mirror is a duplication by necessity (`McpPlugin.NullEngine` cannot reference `McpPlugin.Server`;
they pin different SignalR majors), and the parity assertion in
`ChainRecordReplayTests.InProcessDumpAndMcpRecord_AgreeAfterCanonicalization` is what stops it
drifting.

### Writing an in-process recorder for a real engine

An engine cannot run `McpPlugin.NullEngine` inside its editor, so each engine leg writes its own
recorder against the **existing** `McpPlugin.Common` types — no new plugin API, which is what keeps
it compiling against the pinned DLLs. `McpPlugin.NullEngine/src/Replay/RawDump.cs` is the reference
implementation; the rules it encodes are:

* Drive `IToolManager.RunListTool` once, then `RunCallTool` per battery entry.
* Project each `ResponseData<ResponseCallTool>` the way the server would:
  * outer `Status == Error` → `{"status":"error","content":[{"type":"text","text":<outer Message>}]}`,
    the response's own content **discarded** (that is what `ToolRouter.Call` does);
  * outer `Value == null` → the same shape with `"[Error] Tool returned null value"`;
  * otherwise → status from the inner `Status`, content blocks mapped, `structuredContent` as-is.
* Content-block `type` is one of `text` \| `image` \| `audio` \| `resource`. A **text** block records
  only `type` and `text` — its `MimeType` does not reach the wire. `image` / `audio` record `data`
  (base64) and `mimeType`. `resource` records `{uri, mimeType?, text|blob}`, text preferred over blob.
* `errorKind` is the `ResponseErrorKind` member NAME — `None`, `BadRequest`, `NotFound`, `Conflict`,
  `Timeout`, `Unavailable`, `Internal` — recorded on every call, success included. Parsing is
  case-insensitive on replay, with `Internal` as the fallback for an unknown value.
* Write the lines in any order with any key order: `canonicalize` sorts, masks, caps and computes
  `args_hash`. A raw dump is F1-**shaped**, not canonical.

> **Note.** A `resource` content block is the one shape whose two projections are not proven equal —
> the null engine emits none, so no test covers it. An engine whose tools return embedded resources
> should record with one recorder kind consistently until that gap is closed.

---

## F2 — canonicalisation

`canonicalize` is a pure normal form. It is **idempotent**: canonicalising a canonical fixture
returns the same bytes, which is what lets `diff` compare a committed file against a fresh one.

1. **Keys sorted recursively**, by the key's **UTF-8 bytes**.
2. **No insignificant whitespace** — `,` and `:` separators, nothing else.
3. **Strings**: `"` `\` and the five named control escapes (`\b \f \n \r \t`); any other character
   below `U+0020` as `\u00xx` with lowercase hex; **everything else raw**, non-ASCII included.
   (Escaping non-ASCII instead would mean agreeing on `\uXXXX` casing and surrogate-pair splitting
   across two languages — more to get wrong.)
4. **Numbers keep their original token text.** `2.50`, `1e3` and `-0` survive as written. Re-formatting
   numbers is the single most reliable way for two canonicalisers to disagree.
5. **`null` is preserved** wherever it appears inside a payload — it is content, not an absent field.
   (`null-engine/nested`'s innermost `child` is exactly this shape.) Only the *recorders* omit
   envelope fields they have no value for; canonicalisation never drops anything.
6. **Line order**: `meta` first, then `tool` lines by `name`, then `call` lines by
   `(name, args_hash)` — all by UTF-8 bytes, with a stable sort.

## F3 — masking

**Response subtrees only.** Tool descriptions and schemas are never masked, and neither are `args`
(they are inputs, chosen by the battery). Applied to every **string value** in this order:

| # | rule | becomes |
|---|---|---|
| 1 | `\d{4}-\d{2}-\d{2}[Tt ]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:[Zz]\|[+-]\d{2}:?\d{2})?` | `<ts>` |
| 2 | `[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}` | `<guid>` |
| 3 | `(?<![A-Za-z0-9])[A-Za-z]:[\\/][^\s"'<>\|]*` | `<path>` |
| 4 | `(?<![A-Za-z0-9._\-])/(?:Users\|home\|tmp\|var\|private\|opt\|mnt\|Volumes)(?![A-Za-z0-9])[^\s"'<>\|]*` | `<path>` |
| 5 | `(://[^\s/:"'<>\|]+):\d{1,5}` | *host*`:<port>` |
| 6 | `\b\d+(?:\.\d+)?\s*ms\b` | `<ms>` |

Rule 3's lookbehind is what keeps `http://…` from being read as a drive letter; rule 4's lookahead
is what keeps `/variable` from being read as `/var` plus junk.

Plus a **key-driven** rule, because a pid, port or duration is usually a *number* and no regex over
strings can reach it — the member's whole value becomes the placeholder:

| keys | becomes |
|---|---|
| `pid`, `process_id`, `processId` | `"<pid>"` |
| `port` | `"<port>"` |
| `duration_ms`, `durationMs`, `elapsed_ms`, `elapsedMs` | `"<ms>"` |

Deliberately a short, explicit list. A generic name like `ms` would swallow `null-engine/slow`'s
`slept_ms`, which echoes an **argument** rather than measuring anything — masking it would silently
remove the fixture's ability to discriminate on that field.

### Provenance meta

`recorded_at`, `plugin_version` and `mcp_plugin_version` name **which bits produced the fixture** —
a workspace or future version under a chain or train run — not what the contract is. They are kept
in the file for humans and replaced by a placeholder in the form `diff` compares, so
`p2-record-leg-*`'s "only masked meta differs" holds across a chain run.

`--cross-recorder` additionally neutralises the two **recorder-scoped** fields: `meta.recorder` and
`response.errorKind`. Use it only to compare an in-process capture with an MCP-client one; an engine
leg diffing its own fixture against a fresh recording should not, because both sides used the same
recorder and the strict diff is stronger.

> This is one field more than the three the task specification listed. `meta.recorder` had to join
> them for two recorders to be comparable at all, and it is confined to `--cross-recorder`, so the
> default diff an engine leg runs is unaffected.

## F4 — caps

* A **response** whose canonical JSON exceeds **32 KiB** is replaced by
  `{"$hash":"sha256:<hex>","$bytes":<n>}`, the digest taken over the masked canonical bytes. It still
  discriminates: a changed payload changes the digest.
* A **fixture** whose canonical text exceeds **256 KiB** fails `canonicalize` with **exit 4** and
  `fixture too large: trim the battery`.

## F5 — diff

```
chain_fixture.py diff <committed> <fresh> [--cross-recorder]
```

| exit | meaning |
|---|---|
| 0 | identical |
| 3 | `contract-change`, followed by a unified diff of the canonical (masked) lines |
| 4 | schema mismatch — the two files are not comparable |

A non-zero `diff` fails the engine leg. **The fix is to review the change and commit the new
fixture in the engine repository**, not to relax the diff.

## F6 — replay

`McpPlugin.NullEngine --replay <fixture>` registers one `ReplayTool` per `tool` line — name, title,
description, schemas and hints straight from the fixture — **instead of** its own built-in
catalogue, so a consumer's `tools/list` equals the fixture's tool count exactly. It answers a call
with the response recorded for the same `(name, args_hash)`.

**It never succeeds silently.** Every way of not finding an answer is a distinct, named error:

| condition | answer |
|---|---|
| the fixture publishes the tool but never exercised it | `replay-miss: <name>` |
| the tool was exercised, but not with these arguments | `replay-miss-args: <name> <args_hash>` |
| the recorded response was capped by F4 | `replay-oversize: <hash>` |
| the tool is not in the fixture at all | not registered; the tool manager answers `Tool with Name '<name>' not found.` |

All four reach the MCP client as `isError: true`.

---

## Commands

```bash
# record against a live server (the engine must already be connected)
python scripts/chain_fixture.py record \
    --server-url http://127.0.0.1:8080 \
    --battery tests/chain-fixtures/null-engine/battery.json \
    --engine null-engine --engine-version 0 --surface editor \
    --out tests/chain-fixtures/null-engine/tools.jsonl

# record in-process, no server (this is what an engine leg runs inside its editor)
dotnet McpPlugin.NullEngine.dll --dump-raw raw.jsonl --battery battery.json \
    --engine unity --engine-version 6000.0.23f1 --surface editor
python scripts/chain_fixture.py canonicalize raw.jsonl --out tools.jsonl

# diff a fresh recording against the committed one — exit 3 is a contract change
python scripts/chain_fixture.py diff tests/chain-fixtures/<v>/tools.jsonl fresh.jsonl

# serve a fixture with no engine present
dotnet McpPlugin.NullEngine.dll --mcp-server-endpoint=http://127.0.0.1:8080 \
    --replay tests/chain-fixtures/<v>/tools.jsonl

# the two hashes a vendor check and a battery author need
python scripts/chain_fixture.py hash --file .github/scripts/chain_fixture.py
python scripts/chain_fixture.py hash --args '{"kb":1}'
```

### Exit codes (`chain_fixture.py`)

| code | meaning |
|---|---|
| 0 | success / identical |
| 2 | usage error |
| 3 | contract change |
| 4 | schema mismatch, or a fixture over the size cap |
| 5 | `record` could not drive the server (never a contract verdict) |

## Vendoring

`scripts/chain_fixture.py` is the canonical copy. Engine repositories carry a **byte-identical**
copy at `.github/scripts/chain_fixture.py` (the same rule `chain_feed.py` follows); a vendor check
compares `chain_fixture.py hash --file <path>` against this one. It is stdlib-only and runs on
Python 3.9+, which covers hosted `python3`, Unreal Engine's bundled Python 3.11 and the Windows dev
box. **Never add a third-party import.**

## Adding to a battery

1. Add the call to the engine's `battery.json`.
2. Re-record, and read the diff: it is the contract you are adding.
3. Keep responses under 32 KiB where you can — a capped response cannot be replayed, only diffed.
4. Keep the whole fixture under 256 KiB. If it will not fit, trim the battery; do not raise the cap
   without changing both implementations and this document.
