/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
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
    /// Covers the trusted-internal-client opt-in path: the
    /// <c>X-McpPlugin-Internal-Client</c> request header flips
    /// <see cref="McpSessionTokenContext.IsTrustedInternalClient"/>, which the
    /// MCP <c>list</c> routers consume to skip the <c>Enabled = false</c>
    /// filter, and the <c>To*()</c> extensions tag disabled primitives with
    /// <c>_meta.enabled = false</c> so the trusted client can tell which
    /// entries are off plugin-side.
    ///
    /// The <see cref="McpSessionTokenContext"/> values are <c>AsyncLocal</c>;
    /// these tests reset the relevant slot in <c>finally</c> blocks (or use
    /// the middleware's own cleanup) so state never leaks between cases.
    /// </summary>
    public class TrustedInternalClientTests
    {
        // ─────────────────────────────────────────────────────────────────────
        // Middleware → AsyncLocal flag
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Middleware_SetsTrustedFlag_WhenHeaderEqualsOne()
        {
            // Sanity: AsyncLocal must start clear. If a sibling test polluted
            // the slot, this assertion catches it before we measure anything.
            McpSessionTokenContext.IsTrustedInternalClient.ShouldBeFalse();

            var captured = false;
            await InvokeMiddlewareAsync(
                headers: new() { [Consts.MCP.Server.Headers.TrustedInternalClient] = "1" },
                next: () =>
                {
                    captured = McpSessionTokenContext.IsTrustedInternalClient;
                    return Task.CompletedTask;
                });

            captured.ShouldBeTrue();
            // Middleware MUST clear the slot in `finally` so the flag never
            // bleeds into the next request executing on the same async context.
            McpSessionTokenContext.IsTrustedInternalClient.ShouldBeFalse();
        }

        [Fact]
        public async Task Middleware_LeavesTrustedFlag_False_WhenHeaderAbsent()
        {
            McpSessionTokenContext.IsTrustedInternalClient.ShouldBeFalse();

            var captured = true;
            await InvokeMiddlewareAsync(
                headers: new(),
                next: () =>
                {
                    captured = McpSessionTokenContext.IsTrustedInternalClient;
                    return Task.CompletedTask;
                });

            captured.ShouldBeFalse();
        }

        [Theory]
        [InlineData("0")]
        [InlineData("true")]
        [InlineData("yes")]
        [InlineData("")]
        public async Task Middleware_LeavesTrustedFlag_False_ForUnknownHeaderValues(string headerValue)
        {
            McpSessionTokenContext.IsTrustedInternalClient.ShouldBeFalse();

            var captured = true;
            await InvokeMiddlewareAsync(
                headers: new() { [Consts.MCP.Server.Headers.TrustedInternalClient] = headerValue },
                next: () =>
                {
                    captured = McpSessionTokenContext.IsTrustedInternalClient;
                    return Task.CompletedTask;
                });

            // Only the exact opt-in value flips the flag — anything else stays false.
            // This guards against accidental coercion (e.g. case-insensitive matching).
            captured.ShouldBeFalse();
        }

        [Fact]
        public async Task Middleware_ClearsTrustedFlag_EvenIfNextThrows()
        {
            McpSessionTokenContext.IsTrustedInternalClient.ShouldBeFalse();

            await Should.ThrowAsync<InvalidDataException>(() => InvokeMiddlewareAsync(
                headers: new() { [Consts.MCP.Server.Headers.TrustedInternalClient] = "1" },
                next: () => throw new InvalidDataException("boom")));

            McpSessionTokenContext.IsTrustedInternalClient.ShouldBeFalse();
        }

        // ─────────────────────────────────────────────────────────────────────
        // ExtensionsListMeta — _meta.enabled annotation rule
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void BuildEnabledMeta_ReturnsNull_WhenEnabled()
        {
            // Default-case wire shape MUST be unchanged for enabled primitives —
            // a non-null Meta would surface to every existing third-party client.
            ExtensionsListMeta.BuildEnabledMeta(enabled: true).ShouldBeNull();
        }

        [Fact]
        public void BuildEnabledMeta_ReturnsEnabledFalseObject_WhenDisabled()
        {
            var meta = ExtensionsListMeta.BuildEnabledMeta(enabled: false);

            meta.ShouldNotBeNull();
            meta!.ContainsKey(ExtensionsListMeta.EnabledKey).ShouldBeTrue();
            meta[ExtensionsListMeta.EnabledKey]!.GetValue<bool>().ShouldBeFalse();
        }

        // ─────────────────────────────────────────────────────────────────────
        // To*() extensions — Meta is attached for disabled, omitted for enabled
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void ToTool_OmitsMeta_WhenEnabled()
        {
            new ResponseListTool { Name = "ping", Enabled = true, InputSchema = Consts.MCP.EmptyInputSchema }
                .ToTool()
                .Meta
                .ShouldBeNull();
        }

        [Fact]
        public void ToTool_AttachesEnabledFalseMeta_WhenDisabled()
        {
            var meta = new ResponseListTool { Name = "ping", Enabled = false, InputSchema = Consts.MCP.EmptyInputSchema }.ToTool().Meta;

            meta.ShouldNotBeNull();
            meta![ExtensionsListMeta.EnabledKey]!.GetValue<bool>().ShouldBeFalse();
        }

        [Fact]
        public void ToPrompt_OmitsMeta_WhenEnabled()
        {
            new ResponsePrompt { Name = "p", Enabled = true }
                .ToPrompt()
                .Meta
                .ShouldBeNull();
        }

        [Fact]
        public void ToPrompt_AttachesEnabledFalseMeta_WhenDisabled()
        {
            var meta = new ResponsePrompt { Name = "p", Enabled = false }.ToPrompt().Meta;

            meta.ShouldNotBeNull();
            meta![ExtensionsListMeta.EnabledKey]!.GetValue<bool>().ShouldBeFalse();
        }

        [Fact]
        public void ToResource_OmitsMeta_WhenEnabled()
        {
            new ResponseListResource(uri: "res://x", name: "x", enabled: true)
                .ToResource()
                .Meta
                .ShouldBeNull();
        }

        [Fact]
        public void ToResource_AttachesEnabledFalseMeta_WhenDisabled()
        {
            var meta = new ResponseListResource(uri: "res://x", name: "x", enabled: false)
                .ToResource()
                .Meta;

            meta.ShouldNotBeNull();
            meta![ExtensionsListMeta.EnabledKey]!.GetValue<bool>().ShouldBeFalse();
        }

        [Fact]
        public void ToResourceTemplate_OmitsMeta_WhenEnabled()
        {
            new ResponseResourceTemplate(uri: "tpl://{x}", name: "tpl", enabled: true)
                .ToResourceTemplate()
                .Meta
                .ShouldBeNull();
        }

        [Fact]
        public void ToResourceTemplate_AttachesEnabledFalseMeta_WhenDisabled()
        {
            var meta = new ResponseResourceTemplate(uri: "tpl://{x}", name: "tpl", enabled: false)
                .ToResourceTemplate()
                .Meta;

            meta.ShouldNotBeNull();
            meta![ExtensionsListMeta.EnabledKey]!.GetValue<bool>().ShouldBeFalse();
        }

        // ─────────────────────────────────────────────────────────────────────
        // helpers
        // ─────────────────────────────────────────────────────────────────────

        static async Task InvokeMiddlewareAsync(
            System.Collections.Generic.Dictionary<string, string> headers,
            System.Func<Task> next)
        {
            var ctx = new DefaultHttpContext();
            foreach (var (k, v) in headers)
                ctx.Request.Headers[k] = v;

            var middleware = new McpSessionTokenMiddleware(_ => next());
            await middleware.InvokeAsync(ctx);
        }
    }
}
