# Plant log — the `X-McpPlugin-Skill-Meta` opt-in gate

Verification record for the change that stopped `tools/list` from shipping
`_meta.skillDescription` / `_meta.skillBody` to every caller, and emits them only to clients
presenting `X-McpPlugin-Skill-Meta: 1` — a payload gate, not a secrecy one: the blurb and body
are re-sent in full on every `tools/list`. The gate reads a NEW
`McpSessionTokenContext.IsSkillMetaClient` axis, deliberately not the pre-existing
`IsTrustedInternalClient`.

A green test proves nothing on its own: it may pass for a reason the feature does not supply. Each
claim below was therefore mutated one at a time — the mutation applied to the real source, the
suite re-built and re-run, the source restored byte-exactly — so that every assertion is known to be
capable of failing, and known to fail for **its own** reason.

## How the round was run

- Mutations applied one at a time by a plant-and-revert harness that backs each file up out of tree,
  substitutes bytes, runs the verify command once, restores from that copy, and checks the restore
  by checksum. No `git checkout --` is involved, so uncommitted work is never at risk.
- Verify command, per plant (the build is **inside** the `&&` chain, so no plant is ever scored
  against the previous plant's assemblies):

  ```bash
  dotnet build --no-restore --configuration Release && \
  dotnet test McpPlugin.Server.Tests/McpPlugin.Server.Tests.csproj --no-build --configuration Release \
    --filter "FullyQualifiedName~SkillMetaClientGateTests|FullyQualifiedName~ToolListSkillMetaTests|FullyQualifiedName~TrustedInternalClientTests"
  ```

- Every per-plant log carries a `Passed!`/`Failed!` line with `Total: 46` on both `net8.0` and
  `net9.0`. That the collected count is **46 on all 14 plants** is the check that no RED came from
  an import or compile break rather than from the planted violation.
- Final round: `SUMMARY 14/14 matched their expect | 13 RED | 1 GREEN | restore-failures 0`,
  harness exit `0`.

## The round

Each row names the mutation, the test it reddened, and that test's own runner line. Where a plant
reddened several tests, the row names the one the plant was aimed at; the rest are listed under
*Collateral* below.

The numbering is non-contiguous: the ids `P7` and `P15` appear nowhere in this file. The 14 rows
below are the round in full — their count matches the `14/14` summary above — so no executed plant
is missing from the table; only the id sequence has gaps.

| # | Mutation | Reddened test | Runner output |
|---|---|---|---|
| P1 | `BuildToolMeta`: the opt-in gate deleted entirely | `Middleware_NoSkillMetaHeader_EnabledTool_ProducesTheExactPreSkillMetadataPayload` | `Shouldly.ShouldAssertException : capturedMeta` (a non-opting caller received `_meta`) |
| P2 | `BuildToolMeta`: gate hard-wired open — `if (!McpSessionTokenContext.IsSkillMetaClient)` → `if (!true)` | `Middleware_NoSkillMetaHeader_EnabledTool_ProducesTheExactPreSkillMetadataPayload` | same assertion; the payload is emitted regardless of the header |
| P3 | `BuildToolMeta`: gate reads `IsTrustedInternalClient` instead of the new flag | `Middleware_TrustedInternalClientHeaderAlone_UnlocksDisabledTools_ButNotSkillMetadata` | `Shouldly.ShouldAssertException : disabledMeta!.ToJsonString() should be` — a trusted-client caller started receiving skill metadata |
| P4 | `BuildToolMeta`: the gate returns `null` instead of the pre-existing `_meta` | `Middleware_NoSkillMetaHeader_DisabledTool_ProducesTheExactPreSkillMetadataPayload` | `Shouldly.ShouldAssertException : disabledMeta` — `{"enabled":false}` was dropped for non-opting callers |
| P5 | Middleware: the skill-meta header block deleted, so the flag is never set | `Middleware_SkillMetaHeader_EmitsBothSkillKeys` | `Shouldly.ShouldAssertException : optedIn should not be null but was` |
| P6 | Middleware: ordinal compare replaced by a truthiness test (`!string.IsNullOrEmpty(...)`) | `Middleware_SkillMetaHeader_WithAnyOtherValue_IsTreatedAsAbsent` — 7 of 8 rows | `Shouldly.ShouldAssertException : capturedFlag should be` for `"0"`, `"true"`, `"yes"`, `"TRUE"`, `"01"`, `"1 "`, `" 1"` |
| P8 | `McpSessionTokenContext`: `IsSkillMetaClient` re-pointed at the trusted flag's backing slot | `Context_SettingSkillMetaFlag_LeavesTheTrustedFlagUntouched` | `Shouldly.ShouldAssertException : McpSessionTokenContext.IsSkillMetaClient` — one slot serving both properties |
| P9 | `SelectVisible`: visibility filter re-pointed at the skill flag | `Middleware_SkillMetaHeaderAlone_EmitsSkillMetadata_ButKeepsDisabledToolsHidden` | `Shouldly.ShouldAssertException : visibleNames should be` — opting in to skill metadata also revealed disabled tools |
| P10 | `Consts`: header renamed to `X-McpPlugin-SkillMeta` | `SkillMetaHeader_ConstantsAreTheLiteralWireContract` | `Shouldly.ShouldAssertException : Consts.MCP.Server.Headers.SkillMetaClient should be` |
| P11 | `Consts`: opt-in value changed `"1"` → `"true"` | `SkillMetaHeader_ConstantsAreTheLiteralWireContract` | same assertion on the value constant |
| P12 | `SkillMetaClientScope.OptIn()` made a no-op (`= false`) | `ToTool_EmitsBothSkillKeys_WhenBothPresent` | `Shouldly.ShouldAssertException : meta should not be null but was` |
| P13 | **Pair**: per-flow storage made process-global (`AsyncLocal<bool>` → `StrongBox<bool>`) **and** the `finally` reset removed | `Middleware_SkillMetaFlag_DoesNotLeakIntoTheNextRequest` | `Shouldly.ShouldAssertException : secondFlag should be False but was True` — request 2 inherited request 1's opt-in. Measured **with that test collected alone**; see § *Attributing P13 and P16* |
| P14 | **Half, `expect: green`**: the `finally` reset removed *alone* | *(none — GREEN by design)* | `Passed! - Failed: 0, Passed: 46` on both TFMs |
| P16 | **Pair**: storage made process-global **and** the reset moved out of `finally` into the happy path | `Middleware_ClearsSkillMetaFlag_EvenIfNextThrows` | `Shouldly.ShouldAssertException : McpSessionTokenContext.IsSkillMetaClient should be False but was True` — a throwing request left the flag set. Also measured with that test collected alone |

## Why P14 is GREEN, and why that is not a weak test

The first round ran P14's mutation with `expect: red` and it came back **GREEN: 46/46 passing with
the `finally` reset deleted**. That is a finding about the test, so it was diagnosed rather than
accepted or papered over.

The two leak assertions are guarded by **two independent mechanisms**:

1. **Per-flow storage.** `AsyncLocal<bool>` writes made inside `McpSessionTokenMiddleware.InvokeAsync`
   are not observable by an awaiting caller at all: `AsyncTaskMethodBuilder.Start` saves the
   `ExecutionContext` around the synchronous portion of an async method and restores it on return.
   Two sequential `await`ed middleware invocations are therefore already isolated from each other.
2. **The `finally` reset**, which clears the slot explicitly.

Either mechanism alone satisfies the assertions, so a plant that neuters just one scores GREEN
**correctly**. Deleting the test would have dropped a correct assertion at the moment the system was
shown to be doubly safe. The honest instrument is the simultaneous pair — P13 — which neuters both
at once and reddens the leak test with the exact symptom the reset exists to prevent. P14 is
retained in the table, with `expect: green`, as the recorded proof that the redundancy is real
rather than a rationalisation.

## A withdrawn plant, and why

A third variant — *process-global storage alone*, predicted GREEN as P13's other half — measured
**RED**, and not for the predicted reason. It reddens through **cross-test pollution**: with
process-global storage a write from one test class bleeds into a sibling class running in parallel.
Its failing set was not stable (`net8.0` failed 2 tests, `net9.0` failed 1, different sets), so it is
a race, and a race can carry no reliable `expect_red_marker`. Rather than ship it as an
unattributable RED — or leave a wrong `expect: green` in the table — it was **withdrawn from the
plant table and recorded here as an observation**.

The observation is worth keeping: it means the two mechanisms above are *not* symmetric. The
`finally` reset is genuine defence in depth (removing it alone changes nothing observable), whereas
`AsyncLocal` storage is load-bearing — replacing it with any process-global store makes the server
test suite non-deterministic.

## Attributing P13 and P16 — why these two need the test collected alone

Both plants make the flag's storage process-global. Run inside the full filtered suite, that
contaminates every LATER test in the class, so `Middleware_SkillMetaFlag_DoesNotLeakIntoTheNextRequest`
fails at its OPENING guard — `McpSessionTokenContext.IsSkillMetaClient should be False but was True`,
before its own two-request sequence runs. The test reddens, and the RED is real, but it is charged to
contamination from an earlier test rather than to the leak assertion the plant exists to prove. An
earlier revision of this log recorded a runner line for P13 that belonged to a collateral test
(`gated!.ToJsonString()`, from `Middleware_NoSkillMetaHeader_DisabledTool_...`) for the same reason.

Collecting the aimed test alone removes the contaminating predecessors, so the opening guard cannot
fire and the assertion under test has to carry the RED:

```bash
dotnet build --no-restore --configuration Release && dotnet test McpPlugin.Server.Tests/McpPlugin.Server.Tests.csproj --no-build --configuration Release   --filter "FullyQualifiedName~Middleware_SkillMetaFlag_DoesNotLeakIntoTheNextRequest"
```

Measured that way, `Total: 1` on both TFMs (so the filter matched, and the run is not a zero-match
false green), and the failure is `Shouldly.ShouldAssertException : secondFlag should be False but was
True` — the leak assertion itself. P16 isolated the same way fails on
`McpSessionTokenContext.IsSkillMetaClient`, which in a single-test run can only be its post-throw
assertion. Both claims are therefore non-vacuous; the wider failing sets in the table's own round are
collateral, not the attribution.

## Collateral reddening (not the aimed-at test, listed for completeness)

- **P4** also reddened the pre-existing `TrustedInternalClientTests.ToTool_AttachesEnabledFalseMeta_WhenDisabled`
  (`meta should not be null but was`). That is the `enabled`-contract regression fence firing exactly
  as intended: it proves the untouched a1-era tests really do guard `_meta.enabled` against this
  change, which is the single most important property of a subtractive gate.
- **P1 / P2** additionally reddened several `Middleware_SkillMetaHeader_WithAnyOtherValue_IsTreatedAsAbsent`
  rows and `Middleware_TrustedInternalClientHeaderAlone_...`: with the gate open, every caller sees
  the skill keys, so every "this caller must not" case fails together.
- **P3 / P12** additionally reddened the `ToolListSkillMetaTests` cases, which now run under the
  opt-in scope — the intended coupling, since those tests exist to pin the composition itself.
  P3's blast radius is wider than that line alone suggests, and worth stating because P3 is the plant
  the Definition of Done singles out: it also reddens `Middleware_SkillMetaHeader_EmitsBothSkillKeys`
  and `Middleware_SkillMetaHeaderAlone_EmitsSkillMetadata_ButKeepsDisabledToolsHidden` — with the gate
  keyed off the trusted flag, a caller that sends ONLY the skill-meta header gets no keys at all.
- **P16** additionally reddened two tests via the process-global-storage half it shares with P13
  (see the withdrawn plant above); its aimed-at test failed deterministically on both TFMs.

## What is NOT covered here

- `Consts.ApiVersion` stays `"2.0.0"`. It is unchanged in the diff, which `git diff` shows directly;
  no plant is needed for a constant this change does not touch.
- The `enabled` contract, `BuildEnabledMeta`, and `SelectVisible`'s own behaviour are out of this
  task's scope. `TrustedInternalClientTests.cs` is byte-identical (`git diff` on it is empty) and is
  the regression fence for them; P4 and P9 above show that fence is live.
- ~~**No test drives the gate over the real Streamable HTTP transport.**~~ **CLOSED** by
  [`skill-meta-session-semantics.md`](skill-meta-session-semantics.md) (taskflow a5). The gap
  described below was real and its diagnosis was right: instrumented measurement confirmed that a
  `tools/list` handler observes the `initialize` request's ambient state, so the header on
  `tools/list` alone did nothing. That behaviour is now a stated **per-session** ruling, published
  from `RunSessionHandler` rather than left to middleware ordering, and pinned by a 2×2 matrix driven
  over a real session in both `auth=none` and `auth=oauth`. The paragraph below is kept verbatim as
  the record of what this round did and did not establish.

- **No test drives the gate over the real Streamable HTTP transport.** Every case here reaches the
  middleware directly through a `DefaultHttpContext`, so the assertion and the middleware's
  `AsyncLocal` write share one execution flow. Production does not: `StreamableHttpTransportLayer`
  sets `PerSessionExecutionContext = true`, so a session's request handlers run on the
  `ExecutionContext` captured inside `RunSessionHandler` during `initialize` (that is the documented
  reason `CurrentSessionId` has to be published from there — see `AmbientSessionIdOverHttpTests`).
  Which request's header therefore decides `IsSkillMetaClient` for a `tools/list` handler is NOT
  established by anything in this round, and no assertion here would notice if the answer were
  "none of them". The asymmetry worth checking first: `McpSessionTokenContext.CurrentToken` is
  published from BOTH the middleware and `RunSessionHandler`, and `CurrentSessionId` only from
  `RunSessionHandler` — while both boolean flags are published only from the middleware. The pre-existing `IsTrustedInternalClient` axis has the identical test shape and is
  shipping, so this is not a gap this change introduces — but it is not a gap this change closes
  either, and it is recorded rather than left to be inferred from a green suite. Closing it means an
  over-HTTP case built on the `*OverHttpTests` harness, driving a real `initialize` + `tools/list`
  with and without the header.
- **P1 and P2 are one equivalence class, jointly rather than independently attributed.** Deleting the
  gate and hard-wiring it open are behaviourally identical mutations, so no test can tell them apart
  and they print the same failure line. Measured: their failing sets are identical, test for test, and
  both print `Shouldly.ShouldAssertException : gated should be null but was [...]` on the aimed test.
  Both are kept because the task's Definition of Done names both; the table should not be read as two
  independent proofs.
