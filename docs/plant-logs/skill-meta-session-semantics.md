# Plant log — the `X-McpPlugin-Skill-Meta` opt-in's SESSION semantics

Verification record for the change that gave the skill-metadata opt-in stated semantics, published
them from the point that actually decides them, and pinned them with tests that cross the real
Streamable HTTP transport.

The previous state was not a design. `X-McpPlugin-Skill-Meta: 1` decided `_meta` emission **only
when it rode on `initialize`**; on `tools/list` it did nothing. That was an accident of middleware
ordering, no test crossed the transport, and
[`server-skill-meta-gate.md`](server-skill-meta-gate.md) closes with a section saying exactly that
("**No test drives the gate over the real Streamable HTTP transport**"). This log closes it.

---

## 1. The ruling

> **The opt-in is a property of the SESSION.** On the streamable-HTTP MCP path it is read from the
> `initialize` request and fixed for the session's whole life. A later request's header is ignored —
> it neither turns metadata on nor turns it off. A reconnect is a new session and re-decides from
> scratch. On every other surface (the direct-tool REST endpoints) the flag stays per request.

The same ruling was taken for `X-McpPlugin-Internal-Client`, which shares the ambient context and
had the identical accident.

**Rejected: per-request.** Not on the merits of HTTP convention, but because it is not implementable
on this transport without giving up something worth more:

- `HttpServerTransportOptions.PerSessionExecutionContext` is `true`, so a request handler runs on the
  `ExecutionContext` captured during `initialize` and **cannot observe its own request's ambient
  state at all**. The SDK's own documentation says so: *"If `true`, handlers will get called with the
  same `ExecutionContext` used to call `ConfigureSessionOptions` and `RunSessionHandler`."*
- Turning that option off is what would make a per-request read possible — and it is the thing that
  published `CurrentSessionId` and made sticky engine-instance selection work at all (issue
  #193/#195; `select_engine_instance` failed **100% of the time** before it).
- The remaining route is a session-keyed side table written per request, which two concurrent
  requests on one session would race.

Against that, the opt-in describes a client's appetite for a large payload — a property of a
connection, not of a message. Every other value on this ambient context (token, identity, project
pin, session id) is already session-scoped; a single per-request exception would need its own
mechanism and buy nothing.

**Observable behaviour is unchanged by this change.** That is the point, and it is stated plainly
rather than dressed up: the 2×2 matrix after the fix is byte-for-byte the matrix the probe measured
before it. What changed is that the behaviour is now *decided* at the point that establishes the
session, *documented* where a client author reads it, *independent of pipeline ordering*, and
*pinned* by tests that can tell the two candidate semantics apart.

---

## 2. The mechanism was MEASURED, not assumed

The task specified the mechanism as a hypothesis and required instrumentation. It was instrumented,
and the instrumentation **confirmed** it.

**Instrument.** `AmbientCaptureToolHub` (a fake `IClientToolHub`) snapshots all nine
`McpSessionTokenContext` values from *inside* the MCP request handler — `ToolRouter.ListAll` awaits
it, so it runs in the same frames `ExtensionsListMeta.BuildToolMeta` runs in a moment later. Each
request of one session carries a **different `User-Agent`**, which `McpSessionTokenMiddleware`
copies into the ambient `CurrentUserAgent` on every request. The value the handler observes therefore
*names the request whose `ExecutionContext` it is running on*, discriminating four rival readings:
the `initialize` POST, the `notifications/initialized` POST, its own POST, or none.

`User-Agent` was chosen over a purpose-built probe deliberately: it is an existing production ambient
value written by the real middleware on the real path, so the measurement adds no instrumentation
that could itself be the thing being measured.

**Result** (`PerSessionExecutionContextCaptureTests`, run against the code **before** any fix):

```
handlers must observe the initialize request's ambient state; got
  UserAgent=measure-initialize/1.0, SessionId=kRPbroE8Yz5xmYcAm6e9Ng, ProjectPin=<null>,
  ClientIp=127.0.0.1, AccountId=<null>, Token=<null>, SelectedInstanceId=<null>,
  IsTrustedInternalClient=False, IsSkillMetaClient=False
```

while that same `tools/list` request sent `User-Agent: measure-tools-list/1.0` and the
`notifications/initialized` before it sent `measure-notifications-initialized/1.0`.

**Verdict: the hypothesis is CONFIRMED.** Handlers run on the `initialize` request's
`ExecutionContext`. Three corollaries fell out of the same instrument:

- `IsSkillMetaClient` / `IsTrustedInternalClient` read inside the handler carry the `initialize`
  request's answer, with the header on `tools/list` making no difference.
- `tools/call` is served on the same captured context (measured, not inferred — it is a different SDK
  handler and could have been dispatched differently).
- `CurrentSessionId` is non-null in that snapshot even though `initialize` carries no
  `Mcp-Session-Id` header. That is the existing deliberate re-publication in `RunSessionHandler`, and
  it is the positive artifact that separates "published on purpose" from "frozen by accident".

---

## 3. The 2×2 matrix, reproduced against the fixed build

Driven over a real Streamable HTTP session (`initialize` → `notifications/initialized` →
`tools/list`) by `SkillMetaSessionSemanticsOverHttpTests`. **Both auth modes.** The fixture catalog
is two tools: `gameobject-create` (declares both skill values) and `skill-tool-none` (declares
neither).

| header on `initialize` | header on `tools/list` | `_meta.skillDescription` | `auth=none` | `auth=oauth` |
|---|---|---|---|---|
| no | no | **absent** (negative control) | pass | pass |
| no | **yes** | **absent** | pass | pass |
| **yes** | no | **present** | pass | pass |
| **yes** | **yes** | **present** | pass | pass |

Every cell additionally asserts, before any `_meta` claim is made, that the response carries **both
tools by name** — so "no skill metadata" can never be satisfied by a missing tool, a dropped session
or a JSON-RPC error. The emitting cells assert the exact key **set** and the exact **text**, so
"metadata emitted" cannot be satisfied by an empty object. And `skill-tool-none` is checked to carry
no `_meta` in **every** cell including the emitting ones — the in-cell control that keeps the emitter
attribute-driven rather than blanket.

**The discriminating property** of every cell is *which request carried the header*. Host, tool hub,
tool fixture, HTTP client, JSON-RPC method and arguments are held identical across the four cells.

Three further cases state the ruling as the properties a client author needs:

- `WithinOneSession_TheAnswerNeverChanges_InEitherDirection` — two sessions, four listings: adding
  the header mid-session does not turn metadata on, dropping it mid-session does not turn it off.
- `ANewSessionReDecidesTheOptIn_SoAReconnectWithoutTheHeaderLosesSkillMetadata` — and the FIRST
  session, still alive, still emits afterwards, so the second session's silence is a property of
  that session and not of the host.
- `OptInHeaderOnInitialize_WithAnyOtherValue_DoesNotOptTheSessionIn` — the exact-ordinal rule holds
  at the session-establishing site too, not only in the middleware.

---

## 4. The ambient-value audit

Every value on `McpSessionTokenContext`, classified. The enumeration is **mechanical**:
`AmbientSessionStateAuditTests.EveryAmbientValueIsClassified` reflects over the type's public static
properties and requires the declared table to match exactly, so a tenth value cannot join silently
(plant **P12** below).

| Ambient value | Class | Evidence |
|---|---|---|
| `CurrentToken` | **deliberately re-published** | `StreamableHttpTransportLayer.cs:81` re-reads the `Authorization` header inside `RunSessionHandler`. Measured: a `tools/list` sending `Bearer token-from-tools-list` on a session initialized with `Bearer token-from-initialize` is observed by the handler as `token-from-initialize`. |
| `CurrentSessionId` | **deliberately re-published** | `StreamableHttpTransportLayer.cs:92`, with a comment naming this exact hazard. Measured: non-null in the handler although `initialize` carries no such header — it cannot have come from the request. |
| `IsSkillMetaClient` | **deliberately re-published (this change)** | Was *accidentally frozen*: middleware-only, correct solely because the middleware sits upstream of the capture. Now read from the `initialize` request in `RunSessionHandler`. |
| `IsTrustedInternalClient` | **deliberately re-published (this change)** | The prime suspect the task named, and it was the identical accident. Same fix, zero behaviour change. Pinned over HTTP through catalog VISIBILITY (a different observable from `_meta`, so it cannot pass or fail for the skill gate's reasons) for tools, prompts and resources. |
| `CurrentProjectPin` | **frozen at `initialize`** | Middleware-only. Correct by construction: the pin is a path segment of the client's configured URL, so every request of a session carries the same one. `RunSessionHandler` re-parses it into a local for the reaper (`:120`) and deliberately does not publish it. |
| `CurrentClientIp` | **frozen at `initialize`** | Middleware-only; the connection's remote address cannot change within a session. Measured `127.0.0.1` in the handler. |
| `CurrentUserAgent` | **frozen at `initialize`** | Middleware-only. This is the measurement instrument itself (§2) — its freezing is the observation, not a defect. Read by `WebhookEventCollector`, which will therefore attribute every webhook of a session to the `initialize` request's UA. |
| `CurrentIdentity` | **frozen at `initialize`, and the disagreement is impossible** | Middleware-only. `RunSessionHandler:115` reads it as a fallback for the reaper but never publishes it. **Measured, and NOT what the code predicted:** a later request on the same session presenting a *different account's valid token* is rejected `403 Forbidden: The currently authenticated user does not match the user who initiated the session.` by the MCP SDK's own session/user binding, **before any handler runs** (the plugin-seam counter does not move). The frozen identity can therefore never disagree with the credential on the wire. Pinned, because that control lives in a dependency: an SDK upgrade that relaxed it turns a harmless frozen value into a cross-account routing bug. |
| `CurrentSelectedInstanceId` | **frozen, and compensated** | Middleware-only, and on `initialize` the store is queried with a null session id, so the frozen value is null. `AccountMcpStrategy.ResolveSelection` (`:161-162`) treats null as "ask the store", keyed by `CurrentSessionId` — which is what makes sticky routing work across requests (issue #195). Recorded separately because the frozen slot is **not** the authority: "it works" is evidence about the compensator. |

**"Not alone" was the right prior.** Of nine values, only two were previously published on purpose;
`IsSkillMetaClient` and `IsTrustedInternalClient` were correct by accident, and four more are frozen
harmlessly-but-accidentally.

---

## 5. Ruled in / ruled out

| Surface | Verdict | Evidence |
|---|---|---|
| `tools/call` | **RULED IN** — same captured session context. | Measured: `ToolsCallHandler_AlsoObservesTheAmbientStateOfTheInitializeRequest` sees the `initialize` request's `User-Agent`, not its own. It does not read `IsSkillMetaClient` (no `_meta` on a call result), so nothing about skill metadata changes; the mechanism is shared. |
| `prompts/list`, `resources/list` | **RULED IN for the mechanism, RULED OUT for skill metadata.** | Measured: `PromptAndResourceLists_ShareTheSameSessionScopedVisibilityFlag` — the trusted opt-in on the list request alone is ignored, on `initialize` it governs the whole session, for both prompts and resources. Skill keys can never appear there: `BuildToolMeta` is the sole reader of `IsSkillMetaClient` and has exactly one call site, `ExtensionsTool.ToTool()`, reached only from `ToolRouter.ListAll`; the three non-tool routers assign `BuildEnabledMeta`'s result wholesale. |
| `resources/templates/list` | **RULED IN for the mechanism** (same `SelectVisible` path); no skill keys, same reason as above. Not separately exercised over HTTP — see §8. |
| Session resumption / reconnect | **RULED IN, and it is the ruling rather than a bug.** | Measured: `ANewSessionReDecidesTheOptIn_...` — a re-`initialize` without the header loses skill metadata, while the first session (still alive) keeps emitting. This is the hazard the task named; it is now the documented contract, called out in the client-facing header docs. |
| **`oauth` auth mode** (the task's highest-value unknown) | **RULED OUT as a hazard: authentication does NOT reorder anything relevant.** | The full 2×2 matrix is reproduced under `auth=oauth` with a real ES256 bearer token, `UseAuthentication` populating the principal and `RequireAuthorization` gating the endpoint (`OAuthMcpHost`). All four cells match `auth=none` exactly. Each oauth cell additionally asserts the handler observed the authenticated `AccountId`, so "oauth behaves the same" cannot be satisfied by a host that never authenticated anything. |
| `auth=token` mode | **UNMEASURED.** | It shares `oauth`'s pipeline shape (`RequireAuthorization` on the same endpoints, `TokenAuthenticationHandler` in the same `UseAuthentication` stage) and differs only in how the credential is validated, so the ordering argument carries — but it was not exercised. Listed here so the silence does not read as safety. |
| **stdio transport** | **RULED OUT — no HTTP headers exist**, so `IsSkillMetaClient` is always `false` and skill metadata is never emitted over stdio. Unchanged by this task, and a real gap for any stdio client that wants the catalog. |
| Direct-tool REST (`GET /api/tools`) | **RULED OUT.** | `DirectToolCallEndpoints.ListToolsHandler` builds its own entry objects and never calls `ToTool()`, so it emits no `_meta` at all — with or without the header. The middleware's per-request flag remains correct for this surface. |

---

## 6. The plant round

A green test proves nothing on its own. Each claim below was mutated one at a time against the real
source, rebuilt and re-run, then restored from an out-of-tree backup and the restore verified by
SHA-256. No `git checkout --` is involved, so uncommitted work was never at risk. The harness
asserts its own path is inside this worktree's `.agent-scratch` before it will run (CLAUDE.md
rule 2), so a sibling agent's file of the same name cannot be scored as this one.

Verify command per plant — the build is **inside** the chain, so no plant is ever scored against the
previous plant's assemblies:

```bash
dotnet build McpPlugin.sln --nologo -v q -c Release && \
dotnet test McpPlugin.Server.Tests/McpPlugin.Server.Tests.csproj --no-build -c Release \
  --nologo -v n -f net9.0 --filter "<per-plant filter>"
```

Filters used: **`OVER_HTTP`** = the three new over-HTTP classes (`Total tests: 23`);
**`SKILL_ONLY`** = only the skill-metadata claims, excluding the identity and `User-Agent` controls
(`Total tests: 13`); plus two single-class / single-test filters. The collected `Total` is constant
within each filter across every plant, which is the check that no RED came from a compile or
collection break rather than from the planted violation.

| # | Mutation | Expect | Actual | Reddened | Runner output |
|---|---|---|---|---|---|
| **P1** | `StreamableHttpTransportLayer`: `PerSessionExecutionContext` `true` → **`false`** | red | **red** | 17 of 23. Of the **eight matrix rows** (4 cells × 2 auth modes) exactly **four** reddened — `(init=False, list=True, expect=False)` and `(init=True, list=False, expect=True)` in each mode — and the four non-discriminating diagonal cells stayed **green**. The other 13 REDs are the composites, the ordinal rows, the trusted-flag case, the three capture measurements and two audit cases, all of which depend on the same axis | `Shouldly.ShouldAssertException : attributed.TryGetProperty("_meta", out var meta)` |
| **P2** | `RunSessionHandler`: the skill-meta session publication **deleted** | green | **green** | *(none — GREEN by design)* | `Total tests: 23 / Passed: 23 / Failed: 0` |
| **P3** | Middleware: the skill-meta per-request write **deleted** | green | **green** | *(none, over HTTP)* | `Total tests: 23 / Passed: 23 / Failed: 0` |
| **P3b** | Same mutation as P3, scored against the **middleware's own** tests | red | **red** | 5 — `Middleware_SkillMetaHeader_EmitsBothSkillKeys`, `Middleware_SkillMetaHeaderAlone_...`, `Middleware_SkillMetaFlag_DoesNotLeakIntoTheNextRequest`, both `Middleware_NoSkillMetaHeader_*` | `Shouldly.ShouldAssertException : optedIn` |
| **P4** | **Pair**: the session publication **and** the middleware write both deleted | red | **red** | 12 — every `expectSkillKeys: True` row in both auth modes, plus the capture test's positive control | `Shouldly.ShouldAssertException : attributed.TryGetProperty("_meta", out var meta)`; `… : optedIn.IsSkillMetaClient` |
| **P5** | Session publication reads the **wrong header** (`ReadTrustedInternalClient`), middleware write deleted | red | **red** | 11 — every `expectSkillKeys: True` row | `Shouldly.ShouldAssertException : attributed.TryGetProperty("_meta", out var meta)` |
| **P6** | `BuildToolMeta`: the opt-in gate **deleted** (emit to everyone) | red | **red** | 11 — *exactly* the `expectSkillKeys: **False**` rows in both auth modes, plus the ordinal rows and the composites | same assertion, on the "must carry no `_meta`" branch |
| **P7** | `BuildToolMeta`: gate hard-wired **closed** (emit to nobody) | red | **red** | 11 — *exactly* the `expectSkillKeys: **True**` rows, the mirror image of P6 | same assertion, on the "must carry `_meta`" branch |
| **P8** | `ClientOptInHeaders`: `Ordinal` → `OrdinalIgnoreCase` | red | **green** | *(none)* | **prediction wrong; see §7** |
| **P8b** | `ClientOptInHeaders`: ordinal compare replaced by a **truthiness** test | red | **red** | 5 — *exactly* the five `OptInHeaderOnInitialize_WithAnyOtherValue` rows (`"0"`, `"01"`, `"TRUE"`, `"true"`, `"yes"`), nothing else | `Shouldly.ShouldAssertException : attributed.TryGetProperty("_meta", out var meta)` |
| **P9** | Session publication hard-wired `true` (never reads the header) | red | **red** | 12 — every `expectSkillKeys: False` row plus the ordinal rows and the capture test | same assertion, on the "must carry no `_meta`" branch |
| **P10** | Pipeline: `McpSessionTokenMiddleware` moved **before** `UseRouting()`/`UseAuthentication()` | green | **red** | 5 — **all of them `hub.LastListSnapshot.AccountId`**; zero skill-metadata assertions failed. See §7 | `Shouldly.ShouldAssertException : hub.LastListSnapshot.AccountId` ×10 occurrences, no other assertion |
| **P10s** | Same mutation, scored against `SKILL_ONLY` | green | **green** | *(none)* | `Total tests: 13 / Passed: 13 / Failed: 0` |
| **P11** | Pipeline: `McpSessionTokenMiddleware` **removed entirely** | green | **red** | 8 — `AccountId` (identity gone) and `UserAgent` (nothing writes it any more); zero skill-metadata assertions failed | `… : hub.LastListSnapshot.AccountId`, `… : observed.UserAgent`, `… : snapshot.UserAgent` |
| **P11s** | Same mutation, scored against `SKILL_ONLY` | green | **green** | *(none)* | `Total tests: 13 / Passed: 13 / Failed: 0` |
| **P12** | A tenth property added to `McpSessionTokenContext` | red | **red** | 1 — `EveryAmbientValueIsClassified` | `Shouldly.ShouldAssertException : declared` |

**Arithmetic of the round, stated exactly.** Thirteen plants were predicted up front (P1–P12 plus
P3b); **ten matched on the first attempt**. The three that did not — **P8**, **P10**, **P11** — are
kept in the table with their original prediction and a `MISMATCH` verdict rather than quietly
re-predicted, and each produced a corrected instrument (**P8b**, **P10s**, **P11s**) which then
matched. That is the 16 rows above: 13 agreeing with the expectation as listed, 3 deliberately
recorded as wrong. The two diagnoses are the most informative results in the round and are written
up in §7.

### P6 and P7 are independently attributed

The previous plant log had to record its P1/P2 as one equivalence class because the two mutations
were behaviourally identical. These two are not: "gate always open" and "gate always closed" redden
**disjoint** sets of Theory rows — P6 exactly the `expectSkillKeys: False` rows, P7 exactly the
`expectSkillKeys: True` ones. That disjointness is the evidence that the matrix pins both directions
rather than one direction twice.

### Why P2 and P3 are GREEN, and why that is not a weak test

On the `initialize` request the middleware and `RunSessionHandler` read **the same headers of the
same request**, so on that one request they are redundant by construction. Deleting either alone
therefore leaves the over-HTTP behaviour unchanged — correctly. The honest instrument is the pair:
**P4** removes both and reddens every positive cell.

What each single-site plant *does* prove is different, and both halves are needed:

- **P3 + P3b** together: the middleware's write is no longer load-bearing for the transport
  (P3 green over HTTP) while still being the contract the middleware's own tests pin (P3b red). That
  is the fix working — the transport's behaviour survives the middleware's contribution vanishing.
- **P10s / P11s**: the same conclusion from the pipeline side. With the middleware moved upstream of
  authentication, or deleted from the pipeline outright, **every skill-metadata claim stays green**.
  Before this change the middleware's position was the only thing making the opt-in work.

This is the same shape as the existing gate log's P13/P14 pair, and for the same reason: defence in
depth is real here, so a single-site plant scoring GREEN is a correct measurement, not a hole.

---

## 7. Two wrong predictions, kept

### P8 — a plant that could not fail

`Ordinal` → `OrdinalIgnoreCase` was predicted RED and measured **GREEN**, and the diagnosis is that
the mutation is **behaviourally inert**: the opt-in value is `"1"`, a digit, which has no case. No
input can distinguish the two comparisons while the opt-in value contains no letters. The plant was
not weakened or dropped; it was **replaced** by P8b (ordinal → truthiness), which is the mutation the
assertion actually guards against, and which reddens exactly the five rows it aims at.

Recorded because the general form matters: *a mutation that cannot change behaviour for any input the
test can supply is not a plant.* The transferable consequence here is narrow and worth stating —
**the case-sensitivity of this comparison is untestable while the opt-in value is `"1"`.** What is
testable, and now tested, is that it is not a truthiness test.

### P10 / P11 — the middleware relocation reddened something else entirely

The task asked for a demonstration that the acceptance test goes red when
`McpSessionTokenMiddleware` is moved relative to the SDK's execution-context capture. Two findings:

**First, the literal instruction has no reachable form.** The capture happens *inside* the MCP
endpoint, and an ASP.NET Core request-pipeline middleware cannot be placed after an endpoint handler
begins. Every legal position for the middleware is upstream of the capture, so a legal relocation
cannot move it across. The faithful realisations are therefore (a) move the **capture** — P1,
`PerSessionExecutionContext = false` — and (b) remove the middleware's contribution to the captured
context — P3, P10, P11. All three were run.

**Second, P10 and P11 reddened — but not one skill-metadata assertion.** All 10 failure occurrences
under P10 were `hub.LastListSnapshot.AccountId`, and P11 added `UserAgent`. Moving
`McpSessionTokenMiddleware` **before `UseAuthentication()`** means `HttpContext.User` is not yet
populated when `ConnectionIdentity.FromPrincipal` reads it, so ambient identity becomes null for the
whole session. That is a genuine, previously unpinned ordering hazard on a *different* value, and it
is now caught by the oauth positive control this task added — a control that exists only because the
oauth matrix had to prove it was authenticating anything at all.

So the answer to the acceptance criterion is: **after this change the skill-metadata semantics are
provably ordering-independent** (P10s / P11s, 13/13 green), and the ordering-sensitive plant that
*does* redden the matrix is P1 — which moves the execution-context capture itself, in both
directions, leaving the two non-discriminating cells green. Before this change, P3's mutation would
have taken the semantics with it; that is the whole delta.

---

## 8. What is NOT covered here

- **`Consts.ApiVersion` stays `"2.0.0"` and no `<Version>` moved.** Both are unchanged in the diff,
  which `git diff` shows directly (`git diff --stat -- "*.csproj"` is empty); no plant is needed for
  constants this change does not touch.
- **`auth=token` mode is unmeasured** (see §5). The argument that it behaves like `oauth` is an
  argument, not a measurement.
- **`resources/templates/list` is not separately driven over HTTP.** It shares `SelectVisible` with
  the two lists that are, and carries no skill keys for the same structural reason, but that is
  inference from the call graph rather than an observation.
- **The stdio transport has no opt-in at all** and this change does not give it one. Skill metadata
  is unreachable over stdio by construction; if an stdio client ever needs the catalog, that is a new
  wire mechanism, not a header.
- **The frozen `CurrentIdentity` is protected by a control we do not own.** §4 records the SDK's
  403 and pins it, but the pin is a regression fence, not a guarantee.
- **`CurrentUserAgent` freezing is not fixed.** `WebhookEventCollector` will attribute every webhook
  of a session to the `initialize` request's User-Agent. Classified, not repaired — no consumer has
  been shown to care, and repairing it is a separate decision.
- **This change alters no observable behaviour** (§1). Nothing in this round should be read as
  evidence that a client's experience changed; the round's subject is whether the behaviour is now
  decided, documented and detectable.
