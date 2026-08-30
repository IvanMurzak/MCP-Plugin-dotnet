# Plant log — `ToolTokenCount` measures the published skill metadata

Verification record for the change that taught `ToolTokenCount.Calculate` to measure the
`skillDescription` / `skillBody` payload that `tools/list` already ships in `_meta`, plus the
`SkillMetadataTokenCount` / `EnabledToolsSkillMetadataTokenCount` breakdown.

A green test proves nothing on its own: it may pass for a reason the feature does not supply. Each
claim below was therefore mutated one at a time — the mutation applied to the real source, the suite
re-built and re-run, the source restored byte-exactly — so that every assertion is known to be
capable of failing, and known to fail for **its own** reason.

## How the round was run

- Mutations applied one at a time by a plant-and-revert harness that backs each file up out of tree,
  substitutes bytes, runs the verify command once, restores from that copy, and checks the restore
  by checksum. No `git checkout --` is involved, so uncommitted work is never at risk.
- Verify command, per plant (the build is inside the `&&` chain, so no plant is ever scored against
  the previous plant's assemblies):

  ```bash
  dotnet build McpPlugin.Tests/McpPlugin.Tests.csproj --no-restore --configuration Release \
    && dotnet test McpPlugin.Tests/McpPlugin.Tests.csproj --no-build --configuration Release \
       --filter "FullyQualifiedName~ToolTokenCountSkillMetadataTests"
  ```

- Every plant's log shows `McpPlugin -> …\bin\Release\net8.0\McpPlugin.dll` **and** the `net9.0`
  line, i.e. the planted bytes really were compiled.
- Every plant collected **`Total: 11` on both TFMs** — a constant count across all 13 plants, which
  is what rules out a mutation that reddened the suite by breaking the build/collection instead of
  by breaking the claim.
- All test ids below are under
  `com.IvanMurzak.McpPlugin.Tests.Mcp.ToolTokenCountSkillMetadataTests`.

## Result

```
SUMMARY  13/13 matched their expect | 13 RED | 0 GREEN | restore-failures 0
```

## The plants

| # | Mutation (the claim it removes) | Reddened test | Runner output |
|---|---|---|---|
| P1 | `Calculate`: both skill members deleted — the pre-change instrument | `Calculate_WithSkillMetadata_AddsExactlyTheMeasuredPayload` | `withMetadata should be 85 but was 48` |
| P2 | `Calculate`: only the `skillDescription` member deleted | `Calculate_WithSkillDescriptionOnly_AddsExactlyThatMembersPayload` | `value should be 64 but was 48` |
| P3 | `Calculate`: only the `skillBody` member deleted | `Calculate_WithSkillBodyOnly_AddsExactlyThatMembersPayload` | `value should be 69 but was 48` |
| P4 | `Calculate`: `skillDescription` measured **unconditionally** (the `IsNullOrEmpty` gate dropped) | `Calculate_WithoutSkillMetadata_PinsThePreChangeBaselineExactly` | `nulls should be 48 but was 54` |
| P5 | `Calculate`: `skillBody` measured **unconditionally** | `Calculate_WithoutSkillMetadata_PinsThePreChangeBaselineExactly` | `nulls should be 48 but was 52` |
| P6 | `SkillBodyKey` changed to `"skill_body"` — measured under a key the wire does not use | `Calculate_MeasuresTheSkillMembersUnderTheirPublishedWireKeys` | `ToolTokenCount.SkillBodyKey should be "skillBody" but was "skill_body"` |
| P7 | `CalculateSkillMetadata` returns a constant `0` | `CalculateSkillMetadata_ReturnsTheExactShareOfTheTotal` | `both should be 37 but was 0` |
| P8 | `RunTool.CalculateTokenCount` passes `null, null` instead of `SkillDescription, SkillBody` | `RunTool_TokenCount_MeasuresSkillMetadata_ByExactlyTheMeasuredDelta` | `withMetadata.TokenCount should be 121 but was 84` |
| P9 | `RunTool.CalculateSkillMetadataTokenCount` passes `null, null` | `RunTool_TokenCount_MeasuresSkillMetadata_ByExactlyTheMeasuredDelta` | `withMetadata.SkillMetadataTokenCount should be 37 but was 0` |
| P10 | `ProxyTool.TokenCount` passes `null, null` | `ProxyTool_TokenCount_MeasuresSkillMetadata_AndCachesTheSkillInclusiveValue` | `withMetadata.TokenCount should be 85 but was 48` |
| P11 | `ProxyTool.SkillMetadataTokenCount` passes `null, null` | `ProxyTool_TokenCount_MeasuresSkillMetadata_AndCachesTheSkillInclusiveValue` | `withMetadata.SkillMetadataTokenCount should be 37 but was 0` |
| P12 | `McpToolManager.EnabledToolsSkillMetadataTokenCount` drops the `.Where(… Enabled)` filter | `EnabledToolsSkillMetadataTokenCount_ExcludesDisabledTools` | `toolManager.EnabledToolsSkillMetadataTokenCount should be 0 but was 37` |
| P13 | `McpToolManager` stops overriding the breakdown, so `IToolManager`'s default-interface `0` is served | `EnabledToolsSkillMetadataTokenCount_IsTheShareOfTheEnabledCatalogue` | `toolManager.EnabledToolsSkillMetadataTokenCount should be 37 but was 0` |

## Why the plants are decomposed this way

- **Both directions, as separate plants.** P1–P3 remove the measurement (a tool that ships skill
  metadata must count *more*); P4–P5 make it unconditional (a tool that ships none must count
  *exactly what it counted before*). Neither direction can stand in for the other: a constant
  `+1` survives every "greater than" assertion, and an always-on member survives every
  "the payload is measured" assertion.
- **One plant per independently breakable half.** P1 is the bundled revert the change's own
  acceptance criteria name, and a bundle reddens through whichever half fires first — so the
  `skillDescription` half (P2) and the `skillBody` half (P3) each get their own plant, as do the
  total (P8/P10) and the breakdown (P9/P11) on each of the two `IRunTool` implementations.
- **Three tests are named by two plants each** (P4/P5, P8/P9, P10/P11). That is only evidence if
  the two reds are *tellable apart*, so each pair's runner output above is quoted from its own
  per-plant log and the two differ: `54` vs `52` (the two null keys are different lengths),
  and `TokenCount` vs `SkillMetadataTokenCount` for both wiring pairs.
- **No `> 0` / `>=` assertion is used anywhere in the suite.** The counter has a
  "no skill metadata" branch that satisfies any bound for free; only an exact figure can tell a
  measured payload from an unmeasured one. The expected figures are literals derived from the
  fixture's own JSON length, not recomputed by the test from the implementation.
- **The `+37` delta is base-independent by construction.** The two fixture strings are sized so the
  skill members add exactly 148 characters — a multiple of the 4-characters-per-token divisor — so
  `ceil((L + 148)/4) - ceil(L/4) = 37` whatever `L` is. That is what lets the reflection-based
  `RunTool` assertion pin an exact delta over an input schema the test does not author.
