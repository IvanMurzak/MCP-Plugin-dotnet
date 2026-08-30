/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server.Auth;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// Covers the skill-metadata opt-in gate: the <c>X-McpPlugin-Skill-Meta</c> request header
    /// flips <see cref="McpSessionTokenContext.IsSkillMetaClient"/>, which
    /// <c>ExtensionsListMeta.BuildToolMeta</c> consumes to decide whether a <c>tools/list</c>
    /// entry carries <c>_meta.skillDescription</c> / <c>_meta.skillBody</c>.
    ///
    /// <para>Every fixture here is a tool that DOES declare both skill values, so a "no skill key
    /// was emitted" outcome can only come from the gate — never from an empty fixture. Each such
    /// case is paired with a positive control on the SAME fixture, run with the header, which is
    /// what proves the tool's metadata was reachable at all.</para>
    ///
    /// <para>The second thing under test is that this is a genuinely SEPARATE axis from
    /// <see cref="McpSessionTokenContext.IsTrustedInternalClient"/>. That is asserted twice over:
    /// on the flags themselves (neither setter moves the other slot) and on OBSERVABLE behaviour
    /// (each header alone unlocks its own payload and nothing else) — the flag-level pair catches a
    /// conflated backing field, the behaviour-level pair catches the gate being wired to the wrong
    /// flag, and neither substitutes for the other.</para>
    ///
    /// <para><see cref="McpSessionTokenContext"/> values are <c>AsyncLocal</c>; these tests either
    /// let the middleware's own <c>finally</c> clean up or restore the slot themselves, so state
    /// never leaks between cases.</para>
    /// </summary>
    public class SkillMetaClientGateTests
    {
        const string SkillDescriptionText = "Creates a primitive object in the scene";
        const string SkillBodyText = "# GameObject create\n\nUse `path` to nest the new object.";

        /// <summary>The exact <c>_meta</c> payload a pre-skill-metadata server produced for a DISABLED tool.</summary>
        const string PreSkillMetadataDisabledPayload = "{\"enabled\":false}";

        /// <summary>A tool that declares BOTH skill values — the fixture every gate assertion runs on.</summary>
        static ResponseListTool AttributedTool(bool enabled = true)
            => new ResponseListTool
            {
                Name = "gameobject-create",
                Enabled = enabled,
                InputSchema = Consts.MCP.EmptyInputSchema,
                SkillDescription = SkillDescriptionText,
                SkillBody = SkillBodyText
            };

        // ─────────────────────────────────────────────────────────────────────
        // Wire contract — the header name/value are shared with the app client
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void SkillMetaHeader_ConstantsAreTheLiteralWireContract()
        {
            // Every other assertion in this file sends the header THROUGH these constants, so a
            // changed value would rename the header for every client while reddening nothing.
            // The app's own `SKILL_META_HEADER` / `SKILL_META_HEADER_VALUE` carry these exact
            // strings; the two halves only meet on the wire, so each side must pin them literally.
            Consts.MCP.Server.Headers.SkillMetaClient.ShouldBe("X-McpPlugin-Skill-Meta");
            Consts.MCP.Server.Headers.SkillMetaClientOptInValue.ShouldBe("1");

            // ...and it is its OWN header. Reaching for the trusted-client constant by copy-paste
            // is the single likeliest way to silently re-merge the two axes at the wire level.
            Consts.MCP.Server.Headers.SkillMetaClient
                .ShouldNotBe(Consts.MCP.Server.Headers.TrustedInternalClient);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Header present → the skill keys are emitted
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Middleware_SkillMetaHeader_EmitsBothSkillKeys()
        {
            McpSessionTokenContext.IsSkillMetaClient.ShouldBeFalse();

            var capturedFlag = false;
            System.Text.Json.Nodes.JsonObject? capturedMeta = null;

            await InvokeMiddlewareAsync(
                headers: new() { [Consts.MCP.Server.Headers.SkillMetaClient] = "1" },
                next: () =>
                {
                    capturedFlag = McpSessionTokenContext.IsSkillMetaClient;
                    capturedMeta = AttributedTool().ToTool().Meta;
                    return Task.CompletedTask;
                });

            capturedFlag.ShouldBeTrue();
            capturedMeta.ShouldNotBeNull();
            capturedMeta![ExtensionsListMeta.SkillDescriptionKey]!.GetValue<string>().ShouldBe(SkillDescriptionText);
            capturedMeta[ExtensionsListMeta.SkillBodyKey]!.GetValue<string>().ShouldBe(SkillBodyText);
            // The exact key SET, so an ENABLED tool is also proven not to gain an `enabled` key.
            capturedMeta.Select(kv => kv.Key).ShouldBe(
                new[] { ExtensionsListMeta.SkillDescriptionKey, ExtensionsListMeta.SkillBodyKey });

            // Cleared in the middleware's `finally`, exactly like the trusted flag.
            McpSessionTokenContext.IsSkillMetaClient.ShouldBeFalse();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Header absent → the payload is byte-identical to the pre-skill-metadata server's
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Middleware_NoSkillMetaHeader_EnabledTool_ProducesTheExactPreSkillMetadataPayload()
        {
            // An ENABLED tool's pre-skill-metadata `_meta` was `null` — BuildEnabledMeta's own
            // contract, assigned wholesale. Asserting the WHOLE payload rather than "the skill keys
            // are missing": a null cannot be satisfied by a partially-built object.
            System.Text.Json.Nodes.JsonObject? gated = null;
            await InvokeMiddlewareAsync(
                headers: new(),
                next: () => { gated = AttributedTool().ToTool().Meta; return Task.CompletedTask; });

            gated.ShouldBeNull();

            // Positive control, SAME fixture: with the header the very same tool emits both keys.
            // Without this, `gated == null` would be equally satisfied by a fixture that carried no
            // skill metadata in the first place, and the assertion above would prove nothing.
            System.Text.Json.Nodes.JsonObject? optedIn = null;
            await InvokeMiddlewareAsync(
                headers: new() { [Consts.MCP.Server.Headers.SkillMetaClient] = "1" },
                next: () => { optedIn = AttributedTool().ToTool().Meta; return Task.CompletedTask; });

            optedIn.ShouldNotBeNull();
            optedIn!.Select(kv => kv.Key).ShouldBe(
                new[] { ExtensionsListMeta.SkillDescriptionKey, ExtensionsListMeta.SkillBodyKey });
        }

        [Fact]
        public async Task Middleware_NoSkillMetaHeader_DisabledTool_ProducesTheExactPreSkillMetadataPayload()
        {
            // A DISABLED tool's pre-skill-metadata `_meta` was exactly `{"enabled":false}`. The
            // serialized text is the whole-payload artifact: it pins the key set, the value, AND
            // that nothing else rode along.
            System.Text.Json.Nodes.JsonObject? gated = null;
            await InvokeMiddlewareAsync(
                headers: new(),
                next: () => { gated = AttributedTool(enabled: false).ToTool().Meta; return Task.CompletedTask; });

            gated.ShouldNotBeNull();
            gated!.ToJsonString().ShouldBe(PreSkillMetadataDisabledPayload);

            // Positive control, SAME fixture: with the header all three keys appear, so the payload
            // above is short because of the gate and not because the tool had nothing to publish.
            System.Text.Json.Nodes.JsonObject? optedIn = null;
            await InvokeMiddlewareAsync(
                headers: new() { [Consts.MCP.Server.Headers.SkillMetaClient] = "1" },
                next: () => { optedIn = AttributedTool(enabled: false).ToTool().Meta; return Task.CompletedTask; });

            optedIn.ShouldNotBeNull();
            optedIn!.Select(kv => kv.Key).ShouldBe(new[]
            {
                ExtensionsListMeta.EnabledKey,
                ExtensionsListMeta.SkillDescriptionKey,
                ExtensionsListMeta.SkillBodyKey
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // Wrong header value → treated as absent (ordinal compare, not truthiness)
        // ─────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("0")]
        [InlineData("true")]
        [InlineData("yes")]
        [InlineData("TRUE")]
        [InlineData("01")]
        [InlineData("1 ")]
        [InlineData(" 1")]
        [InlineData("")]
        public async Task Middleware_SkillMetaHeader_WithAnyOtherValue_IsTreatedAsAbsent(string headerValue)
        {
            McpSessionTokenContext.IsSkillMetaClient.ShouldBeFalse();

            var capturedFlag = true;
            System.Text.Json.Nodes.JsonObject? capturedMeta = null;

            await InvokeMiddlewareAsync(
                headers: new() { [Consts.MCP.Server.Headers.SkillMetaClient] = headerValue },
                next: () =>
                {
                    capturedFlag = McpSessionTokenContext.IsSkillMetaClient;
                    capturedMeta = AttributedTool().ToTool().Meta;
                    return Task.CompletedTask;
                });

            // Only the exact opt-in value opts in. An ordinal comparison is what makes "TRUE",
            // "01" and a value with stray whitespace all fail; a truthiness or case-insensitive
            // test would let several of these through.
            capturedFlag.ShouldBeFalse();
            capturedMeta.ShouldBeNull();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Independence of the two axes — at the FLAG level, both directions
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void Context_SettingSkillMetaFlag_LeavesTheTrustedFlagUntouched()
        {
            McpSessionTokenContext.IsSkillMetaClient.ShouldBeFalse();
            McpSessionTokenContext.IsTrustedInternalClient.ShouldBeFalse();

            try
            {
                McpSessionTokenContext.IsSkillMetaClient = true;

                McpSessionTokenContext.IsSkillMetaClient.ShouldBeTrue();
                // One backing slot for both properties would make this read `true`.
                McpSessionTokenContext.IsTrustedInternalClient.ShouldBeFalse();
            }
            finally
            {
                McpSessionTokenContext.IsSkillMetaClient = false;
            }
        }

        [Fact]
        public void Context_SettingTrustedFlag_LeavesTheSkillMetaFlagUntouched()
        {
            McpSessionTokenContext.IsSkillMetaClient.ShouldBeFalse();
            McpSessionTokenContext.IsTrustedInternalClient.ShouldBeFalse();

            try
            {
                McpSessionTokenContext.IsTrustedInternalClient = true;

                McpSessionTokenContext.IsTrustedInternalClient.ShouldBeTrue();
                // The mirror of the case above. Asserting only ONE direction cannot distinguish a
                // shared slot from a one-way alias, so both directions are required.
                McpSessionTokenContext.IsSkillMetaClient.ShouldBeFalse();
            }
            finally
            {
                McpSessionTokenContext.IsTrustedInternalClient = false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Independence of the two axes — at the BEHAVIOUR level, both directions
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Middleware_TrustedInternalClientHeaderAlone_UnlocksDisabledTools_ButNotSkillMetadata()
        {
            // The load-bearing case for keeping the axes split. If BuildToolMeta read
            // IsTrustedInternalClient instead of IsSkillMetaClient, the skill keys below would
            // appear for a caller that only ever asked for the unfiltered catalog.
            var visibleNames = new List<string>();
            System.Text.Json.Nodes.JsonObject? disabledMeta = null;
            System.Text.Json.Nodes.JsonObject? enabledMeta = null;

            await InvokeMiddlewareAsync(
                headers: new() { [Consts.MCP.Server.Headers.TrustedInternalClient] = "1" },
                next: () =>
                {
                    visibleNames.AddRange(Catalog().SelectVisible(x => x.Enabled, x => x.Name!));
                    disabledMeta = AttributedTool(enabled: false).ToTool().Meta;
                    enabledMeta = AttributedTool().ToTool().Meta;
                    return Task.CompletedTask;
                });

            // The trusted axis IS on — a positive artifact, so this cannot pass by nothing happening.
            visibleNames.ShouldBe(new[] { "enabled-tool", "disabled-tool" });

            // ...and the skill axis is NOT: both payloads are exactly the pre-skill-metadata shapes.
            disabledMeta.ShouldNotBeNull();
            disabledMeta!.ToJsonString().ShouldBe(PreSkillMetadataDisabledPayload);
            enabledMeta.ShouldBeNull();
        }

        [Fact]
        public async Task Middleware_SkillMetaHeaderAlone_EmitsSkillMetadata_ButKeepsDisabledToolsHidden()
        {
            // The mirror: opting in to skill metadata must not widen catalog visibility. A single
            // shared flag — or SelectVisible learning to read the skill flag — reddens here.
            var visibleNames = new List<string>();
            System.Text.Json.Nodes.JsonObject? enabledMeta = null;

            await InvokeMiddlewareAsync(
                headers: new() { [Consts.MCP.Server.Headers.SkillMetaClient] = "1" },
                next: () =>
                {
                    visibleNames.AddRange(Catalog().SelectVisible(x => x.Enabled, x => x.Name!));
                    enabledMeta = AttributedTool().ToTool().Meta;
                    return Task.CompletedTask;
                });

            // Exactly the enabled tool survives — named, not merely counted.
            visibleNames.ShouldBe(new[] { "enabled-tool" });

            // ...while the skill keys DO reach this caller (the positive half of the same claim).
            enabledMeta.ShouldNotBeNull();
            enabledMeta!.Select(kv => kv.Key).ShouldBe(
                new[] { ExtensionsListMeta.SkillDescriptionKey, ExtensionsListMeta.SkillBodyKey });
        }

        // ─────────────────────────────────────────────────────────────────────
        // The flag must not survive its request
        //
        // Both cases below are guarded by TWO INDEPENDENT mechanisms, and that is worth stating
        // because it changes how they must be verified. (1) The flag's storage is per-flow: an
        // AsyncLocal write inside the middleware is not observable by an awaiting caller at all,
        // because AsyncTaskMethodBuilder.Start restores the ExecutionContext around the
        // synchronous part of an async method. (2) The middleware clears the slot in its own
        // `finally`. Either mechanism alone satisfies these assertions, so a plant that neuters
        // just ONE of them scores GREEN *correctly* — measured: deleting the `finally` reset left
        // all 46 tests passing. That is not a vacuous test, it is defence in depth, and the honest
        // proof is the simultaneous pair: make the storage process-global AND drop the reset, and
        // both cases below redden. Do not "fix" a single-site GREEN here by weakening the test.
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Middleware_SkillMetaFlag_DoesNotLeakIntoTheNextRequest()
        {
            McpSessionTokenContext.IsSkillMetaClient.ShouldBeFalse();

            // Request 1 opts in.
            var firstFlag = false;
            await InvokeMiddlewareAsync(
                headers: new() { [Consts.MCP.Server.Headers.SkillMetaClient] = "1" },
                next: () => { firstFlag = McpSessionTokenContext.IsSkillMetaClient; return Task.CompletedTask; });

            firstFlag.ShouldBeTrue();

            // Request 2, on the SAME execution context, sends no header. The middleware only WRITES
            // the flag when the header is present, so nothing here overwrites a stale `true` — the
            // `finally` reset of request 1 is the only thing standing between the two, which is
            // exactly the pooled-request hazard this asserts against.
            var secondFlag = true;
            System.Text.Json.Nodes.JsonObject? secondMeta = null;
            await InvokeMiddlewareAsync(
                headers: new(),
                next: () =>
                {
                    secondFlag = McpSessionTokenContext.IsSkillMetaClient;
                    secondMeta = AttributedTool().ToTool().Meta;
                    return Task.CompletedTask;
                });

            secondFlag.ShouldBeFalse();
            // Asserted on the OBSERVABLE payload too: the bool is the mechanism, the withheld
            // skill metadata is the consequence a client would actually see.
            secondMeta.ShouldBeNull();
        }

        [Fact]
        public async Task Middleware_ClearsSkillMetaFlag_EvenIfNextThrows()
        {
            McpSessionTokenContext.IsSkillMetaClient.ShouldBeFalse();

            await Should.ThrowAsync<InvalidDataException>(() => InvokeMiddlewareAsync(
                headers: new() { [Consts.MCP.Server.Headers.SkillMetaClient] = "1" },
                next: () => throw new InvalidDataException("boom")));

            McpSessionTokenContext.IsSkillMetaClient.ShouldBeFalse();
        }

        // ─────────────────────────────────────────────────────────────────────
        // helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>One enabled and one disabled tool — the input <c>SelectVisible</c> filters.</summary>
        static List<ResponseListTool?> Catalog() => new()
        {
            new ResponseListTool { Name = "enabled-tool", Enabled = true, InputSchema = Consts.MCP.EmptyInputSchema },
            new ResponseListTool { Name = "disabled-tool", Enabled = false, InputSchema = Consts.MCP.EmptyInputSchema }
        };

        static async Task InvokeMiddlewareAsync(Dictionary<string, string> headers, Func<Task> next)
        {
            var ctx = new DefaultHttpContext();
            foreach (var (k, v) in headers)
                ctx.Request.Headers[k] = v;

            var middleware = new McpSessionTokenMiddleware(_ => next());
            await middleware.InvokeAsync(ctx);
        }
    }
}
