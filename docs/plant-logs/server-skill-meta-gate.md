# Plant log — the `X-McpPlugin-Skill-Meta` opt-in gate

Verification record for the change that stopped `tools/list` from shipping
`_meta.skillDescription` / `_meta.skillBody` to every caller, and emits them only to clients
presenting `X-McpPlugin-Skill-Meta: 1` (owner ruling F1). The gate reads a NEW
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
| P13 | **Pair**: per-flow storage made process-global (`AsyncLocal<bool>` → `StrongBox<bool>`) **and** the `finally` reset removed | `Middleware_SkillMetaFlag_DoesNotLeakIntoTheNextRequest` | `Shouldly.ShouldAssertException : gated!.ToJsonString()` — request 2 inherited request 1's opt-in |
| P14 | **Half, `expect: green`**: the `finally` reset removed *alone* | *(none — GREEN by design)* | `Passed! - Failed: 0, Passed: 46` on both TFMs |
| P16 | **Pair**: storage made process-global **and** the reset moved out of `finally` into the happy path | `Middleware_ClearsSkillMetaFlag_EvenIfNextThrows` | `Shouldly.ShouldAssertException` — a throwing request left the flag set |

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
- **P16** additionally reddened two tests via the process-global-storage half it shares with P13
  (see the withdrawn plant above); its aimed-at test failed deterministically on both TFMs.

## What is NOT covered here

- `Consts.ApiVersion` stays `"2.0.0"`. It is unchanged in the diff, which `git diff` shows directly;
  no plant is needed for a constant this change does not touch.
- The `enabled` contract, `BuildEnabledMeta`, and `SelectVisible`'s own behaviour are out of this
  task's scope. `TrustedInternalClientTests.cs` is byte-identical (`git diff` on it is empty) and is
  the regression fence for them; P4 and P9 above show that fence is live.
