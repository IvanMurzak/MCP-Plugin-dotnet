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
- Every plant collected **`Total: 15`** — a constant count across all 19 plants, which is what rules
  out a mutation that reddened the suite by breaking the build/collection instead of by breaking the
  claim. Every plant's log also carries a `Failed!` line: a filter matching nothing exits 0, so a
  non-zero `Total:` is the only thing separating a real red from a run that collected no tests.
- All test ids below are under
  `com.IvanMurzak.McpPlugin.Tests.Mcp.ToolTokenCountSkillMetadataTests`.

## Result

```
SUMMARY  19/19 matched their expect | 19 RED | 0 GREEN | restore-failures 0
```

The round was re-run and extended after review. P1-P13 are the original round; P4 and P5 were
**re-pointed** onto the single-member tests, where each reddens a test the other leaves green
(before, both named the baseline test and differed only by an integer). P14-P19 cover claims the
review found asserted but unproven — the empty-string gate, the `skillDescription` key spelling,
the share guard's single-member path, that the cache exists at all, base-independence, and that the
catalogue aggregation is an addition rather than a maximum.

## The plants

| # | Mutation (the claim it removes) | Reddened test | Runner output |
|---|---|---|---|
| P1 | `Calculate`: both skill members deleted — the pre-change instrument | `Calculate_WithSkillMetadata_AddsExactlyTheMeasuredPayload` | `withMetadata should be 85 but was 48` |
| P2 | `Calculate`: only the `skillDescription` member deleted | `Calculate_WithSkillDescriptionOnly_AddsExactlyThatMembersPayload` | `value should be 64 but was 48` |
| P3 | `Calculate`: only the `skillBody` member deleted | `Calculate_WithSkillBodyOnly_AddsExactlyThatMembersPayload` | `value should be 69 but was 48` |
| P4 | `Calculate`: `skillDescription` measured **unconditionally** (the `IsNullOrEmpty` gate dropped) | `Calculate_WithSkillBodyOnly_AddsExactlyThatMembersPayload` | `value should be 69 but was 75` |
| P5 | `Calculate`: `skillBody` measured **unconditionally** | `Calculate_WithSkillDescriptionOnly_AddsExactlyThatMembersPayload` | `value should be 64 but was 68` |
| P6 | `SkillBodyKey` changed to `"skill_body"` — measured under a key the wire does not use | `Calculate_MeasuresTheSkillMembersUnderTheirPublishedWireKeys` | `ToolTokenCount.SkillBodyKey should be "skillBody" but was "skill_body"` |
| P7 | `CalculateSkillMetadata` returns a constant `0` | `CalculateSkillMetadata_ReturnsTheExactShareOfTheTotal` | `both should be 37 but was 0` |
| P8 | `RunTool.CalculateTokenCount` passes `null, null` instead of `SkillDescription, SkillBody` | `RunTool_TokenCount_MeasuresSkillMetadata_ByExactlyTheMeasuredDelta` | `withMetadata.TokenCount should be 121 but was 84` |
| P9 | `RunTool.CalculateSkillMetadataTokenCount` passes `null, null` | `RunTool_TokenCount_MeasuresSkillMetadata_ByExactlyTheMeasuredDelta` | `withMetadata.SkillMetadataTokenCount should be 37 but was 0` |
| P10 | `ProxyTool.TokenCount` passes `null, null` | `ProxyTool_TokenCount_MeasuresSkillMetadata_AndCachesTheSkillInclusiveValue` | `withMetadata.TokenCount should be 85 but was 48` |
| P11 | `ProxyTool.SkillMetadataTokenCount` passes `null, null` | `ProxyTool_TokenCount_MeasuresSkillMetadata_AndCachesTheSkillInclusiveValue` | `withMetadata.SkillMetadataTokenCount should be 37 but was 0` |
| P12 | `McpToolManager.EnabledToolsSkillMetadataTokenCount` drops the `.Where(… Enabled)` filter | `EnabledToolsSkillMetadataTokenCount_ExcludesDisabledTools` | `toolManager.EnabledToolsSkillMetadataTokenCount should be 13 but was 50` |
| P13 | `McpToolManager` stops overriding the breakdown, so `IToolManager`'s default-interface `0` is served | `EnabledToolsSkillMetadataTokenCount_IsTheShareOfTheEnabledCatalogue` | `toolManager.EnabledToolsSkillMetadataTokenCount should be 50 but was 0` |
| P14 | `Calculate`: the `skillDescription` gate weakened from `!IsNullOrEmpty` to `!= null` — an **empty string** is then measured | `Calculate_WithoutSkillMetadata_PinsThePreChangeBaselineExactly` | `empties should be 48 but was 53` (`an EMPTY-STRING skill member must cost exactly nothing`) |
| P15 | `SkillDescriptionKey` changed to `"skill_description"` — P6's mirror, so neither key spelling stands in for the other | `Calculate_MeasuresTheSkillMembersUnderTheirPublishedWireKeys` | `ToolTokenCount.SkillDescriptionKey should be "skillDescription" but was "skill_description"` |
| P16 | `CalculateSkillMetadata`: the guard's `&&` weakened to `||`, so a tool carrying only ONE member reports no share | `CalculateSkillMetadata_ReturnsTheExactShareOfTheTotal` | `descriptionOnly should be 16 but was 0` |
| P17 | `RunTool.SkillMetadataTokenCount` returns without ever writing its cache field | `RunTool_CachesTheSkillInclusiveCounts` | `value should not be null but was` |
| P18 | `Calculate`: the measured `skillBody` payload inflated by **one character** | `Calculate_TheSkillMetadataDelta_IsIndependentOfTheBaseLength` | `with - without should be 37 but was 38` (`base padded by 2 character(s)`) |
| P19 | `McpToolManager`: the catalogue breakdown's `.Sum(…)` replaced by `.Max(…)` | `EnabledToolsSkillMetadataTokenCount_IsTheShareOfTheEnabledCatalogue` | `toolManager.EnabledToolsSkillMetadataTokenCount should be 50 but was 37` |

## Why the plants are decomposed this way

- **Both directions, as separate plants.** P1–P3 remove the measurement (a tool that ships skill
  metadata must count *more*); P4–P5 and P14 make it unconditional (a tool that ships none must count
  *exactly what it counted before* — P4/P5 for a `null` member, P14 for an empty string, which the
  gate is separately required to treat as free). Neither direction can stand in for the other: a
  constant `+1` survives every "greater than" assertion, and an always-on member survives every
  "the payload is measured" assertion.
- **One plant per independently breakable half.** P1 is the bundled revert the change's own
  acceptance criteria name, and a bundle reddens through whichever half fires first — so the
  `skillDescription` half (P2) and the `skillBody` half (P3) each get their own plant, as do the
  total (P8/P10) and the breakdown (P9/P11) on each of the two `IRunTool` implementations, the two
  key spellings (P6/P15), the share guard's both-members and single-member paths (P7/P16), the
  existence of the cache (P17), and the catalogue's filter and its addition (P12/P19).
  **Reached but deliberately unplanted:** the `catch → 0` arms on both implementations (unreachable
  through the public surface — the schemas are detached in the constructor), and
  `CalculateSkillMetadata`'s early return, which is a fast path whose deletion is unobservable
  because `Calculate`'s own gates already make the difference zero. Both are recorded here rather
  than covered, so nobody reads their silence as coverage.
- **Two plants naming one test must fail DIFFERENTLY.** Fourteen plants name seven tests in pairs, so each
  pair's runner output above is reproduced — whitespace collapsed onto one line — from its own
  per-plant log, and no pair prints the same text. The seven pairs are P2/P5, P3/P4, P6/P15, P7/P16,
  P8/P9, P10/P11 and P13/P19. Most differ by which symbol the assertion names (`TokenCount` vs
  `SkillMetadataTokenCount`; `SkillBodyKey` vs `SkillDescriptionKey`; `both` vs `descriptionOnly`).
  **P4/P5 were re-pointed to get that property.** On the baseline test they printed
  `nulls should be 48 but was 54` and `… but was 52` — non-vacuous and correctly attributed, yet
  tellable apart only by knowing that `"skillDescription"` is seven characters longer than
  `"skillBody"`. Moving each onto the single-member test the *other* member's plant leaves green
  (P4 → `…WithSkillBodyOnly`, P5 → `…WithSkillDescriptionOnly`) means a future red names the
  member that regressed. The baseline test keeps its own plant in P14.
- **No `> 0` / `>=` assertion is used anywhere in the suite.** The counter has a
  "no skill metadata" branch that satisfies any bound for free; only an exact figure can tell a
  measured payload from an unmeasured one. The direct-helper and `ProxyTool` expectations are
  literals derived from the fixture's own JSON length. The reflection-based `RunTool` and
  catalogue expectations are exact *relative* figures — a sibling's count plus the literal `37` —
  because the test does not author the reflected input schema; the base-independence below is what
  makes that delta exact rather than approximate.
- **The `+37` delta is base-independent by construction.** The two fixture strings are sized so the
  skill members add exactly 148 characters — a multiple of the 4-characters-per-token divisor — so
  `ceil((L + 148)/4) - ceil(L/4) = 37` whatever `L` is. That is what lets the reflection-based
  `RunTool` assertion pin an exact delta over an input schema the test does not author. It also
  costs: at one base length the count cannot tell 148 characters from 147-150, so the delta is
  additionally asserted over four base lengths covering every residue modulo the divisor, and
  P18 (payload inflated by one character) reddens exactly the row that can see it.
