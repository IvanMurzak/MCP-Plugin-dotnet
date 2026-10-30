/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

#nullable enable
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// Tool-call arguments, the one deadline and plugin reloads, through the real MCP and REST pipelines
    /// with real SignalR plugins (wave-4/5 findings 2-5).
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class ToolCallGuardTests
    {
        // ───────────────────────────── arguments ─────────────────────────────

        [Fact]
        public async Task MissingRequiredArgument_IsRejectedBeforeThePlugin()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Godot 4.5");
            await editor.StartAsync();

            var (isError, text) = await client.CallAsync("rql-query", new { });
            isError.ShouldBeTrue();
            text.ShouldBe("[invalid_params] Invalid params for tool 'rql-query': missing required argument 'query' (a string).");

            var (status, body) = await client.RestAsync("rql-query", "{}");
            status.ShouldBe(400);
            body!["error"]!.GetValue<string>().ShouldContain("missing required argument 'query'");

            editor.Calls.ShouldNotContain(c => c.Name == "rql-query");
        }

        [Fact]
        public async Task WrongArgumentTypes_AreRejected_IntegerAndStringThroughOnePath()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Godot 4.5");
            await editor.StartAsync();

            var (isError, text) = await client.CallAsync("rql-query", new { query = 123 });
            isError.ShouldBeTrue();
            text.ShouldContain("argument 'query' must be a string, got a number (123)");

            (isError, text) = await client.CallAsync("rql-query", new { query = "x", max_targets = "many" });
            isError.ShouldBeTrue();
            text.ShouldContain("argument 'max_targets' must be an integer, got a string");

            (isError, text) = await client.CallAsync("rql-query", new { query = "x", max_targets = 3 });
            isError.ShouldBeFalse(text);
            editor.Calls.Count(c => c.Name == "rql-query").ShouldBe(1);
        }

        [Fact]
        public async Task McpPlugin814Connection_OmittedRequiredArgument_IsRejected()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Godot 4.5");
            editor.McpPluginVersion = "8.14.0";
            await editor.StartAsync();

            var (isError, text) = await client.CallAsync("rql-query", new { });
            isError.ShouldBeTrue();
            text.ShouldContain("missing required argument 'query'");
            editor.Calls.ShouldNotContain(c => c.Name == "rql-query");
        }

        [Fact]
        public async Task OldPluginConnection_OmittedRequiredArgument_IsForwardedAsBefore()
        {
            // A plugin on McpPlugin before 8.14.0 sends no mcpPluginVersion. Its ReflectorNet lists every
            // parameter without a default as required while its binder fills an omitted one, so the call ran.
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Godot 4.5");
            editor.McpPluginVersion = null;
            await editor.StartAsync();

            var (isError, text) = await client.CallAsync("rql-query", new { });
            isError.ShouldBeFalse(text);
            text.ShouldBe("editor:rql-query");

            var (status, _) = await client.RestAsync("rql-query", "{}");
            status.ShouldBe(200);

            // An explicit null binds like an omitted argument in such a plugin: forwarded too.
            (isError, text) = await client.CallAsync("rql-query", new { query = (string?)null });
            isError.ShouldBeFalse(text);
            editor.Calls.Count(c => c.Name == "rql-query").ShouldBe(3);
        }

        [Fact]
        public async Task OldPluginConnection_WrongArgumentTypes_AreForwardedAsBefore()
        {
            // Server 9.2.x validated nothing: an old plugin's own binder decides, e.g. it may parse a JSON string
            // given for an object parameter. The server forwards such calls unchanged.
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Godot 4.5");
            editor.McpPluginVersion = null;
            editor.Tools = editor.Tools.Append(new ResponseListTool
            {
                Name = "set-settings",
                InputSchema = System.Text.Json.JsonDocument.Parse(
                    "{\"type\":\"object\",\"properties\":{\"settings\":{\"type\":\"object\"}},\"required\":[\"settings\"]}").RootElement.Clone(),
            }).ToArray();
            await editor.StartAsync();

            var (isError, text) = await client.CallAsync("set-settings", new { settings = "{\"a\":1}" });
            isError.ShouldBeFalse(text);
            text.ShouldBe("editor:set-settings");

            (isError, text) = await client.CallAsync("rql-query", new { query = 123 });
            isError.ShouldBeFalse(text);
            editor.Calls.Count(c => c.Name == "set-settings").ShouldBe(1);
            editor.Calls.Count(c => c.Name == "rql-query").ShouldBe(1);
        }

        [Fact]
        public async Task OldPluginConnection_ReloadRefusal_IsReturnedAsThePluginSentIt()
        {
            // 9.2.x returned an old plugin's own error at once; the 8.14 guard waits and rewrites it for 8.14 plugins.
            await using var host = await NoneAuthMcpHost.StartAsync(null, "plugin-reconnect-wait-seconds=60");
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Godot 4.5", onCall: (_, request)
                => Task.FromResult(FakeEnginePlugin.Error(request, GodotPendingRefusal, ResponseErrorKind.Unavailable)));
            editor.McpPluginVersion = null;
            await editor.StartAsync();

            var watch = Stopwatch.StartNew();
            var (isError, text) = await client.CallAsync("echo", new { });
            isError.ShouldBeTrue();
            text.ShouldContain(GodotPendingRefusal);
            text.ShouldNotContain("[plugin_reloading]");
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
            editor.Calls.Count(c => c.Name == "echo").ShouldBe(1);
        }

        [Fact]
        public async Task OldPluginConnection_LoneSurrogate_IsStillRefused()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Godot 4.5");
            editor.McpPluginVersion = null;
            await editor.StartAsync();

            var (status, body) = await client.RestAsync("rql-query", "{\"query\":\"a\\udc00\"}");
            status.ShouldBe(400);
            body!["error"]!.GetValue<string>().ShouldStartWith("[invalid_utf16]");
            editor.Calls.ShouldBeEmpty();
        }

        [Fact]
        public async Task McpPlugin814Connection_StringForAnObjectParameter_IsRejected()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Godot 4.5");
            editor.McpPluginVersion = "8.14.0";
            editor.Tools = editor.Tools.Append(new ResponseListTool
            {
                Name = "set-settings",
                InputSchema = System.Text.Json.JsonDocument.Parse(
                    "{\"type\":\"object\",\"properties\":{\"settings\":{\"type\":\"object\"}},\"required\":[\"settings\"]}").RootElement.Clone(),
            }).ToArray();
            await editor.StartAsync();

            var (isError, text) = await client.CallAsync("set-settings", new { settings = "{\"a\":1}" });
            isError.ShouldBeTrue();
            text.ShouldContain("argument 'settings' must be an object");
            editor.Calls.ShouldNotContain(c => c.Name == "set-settings");
        }

        [Fact]
        public async Task LoneSurrogate_GetsAClearError()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Godot 4.5");
            await editor.StartAsync();

            var response = await client.RawAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":42,\"method\":\"tools/call\",\"params\":{\"name\":\"rql-query\",\"arguments\":{\"query\":\"a\\ud800b\"}}}");
            response["error"]!["code"]!.GetValue<int>().ShouldBe(-32602);
            var message = response["error"]!["message"]!.GetValue<string>();
            message.ShouldContain("[invalid_utf16]");
            message.ShouldContain("params.arguments.query");

            var (status, body) = await client.RestAsync("rql-query", "{\"query\":\"a\\udc00\"}");
            status.ShouldBe(400);
            body!["error"]!.GetValue<string>().ShouldContain("arguments.query");
            editor.Calls.ShouldBeEmpty();
        }

        // ───────────────────────────── deadline ─────────────────────────────

        [Fact]
        public async Task HungTool_ReturnsATimeoutErrorAtTheToolTimeout()
        {
            await using var host = await NoneAuthMcpHost.StartAsync(null, "tool-timeout=1500");
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Godot 4.5", onCall: async (plugin, request) =>
            {
                await Task.Delay(Timeout.Infinite, plugin.Disposed); // a blocked engine main thread
                return FakeEnginePlugin.Success(request, "never");
            });
            await editor.StartAsync();

            var watch = Stopwatch.StartNew();
            var (isError, text) = await client.CallAsync("echo", new { });
            isError.ShouldBeTrue();
            text.ShouldStartWith("[tool_timeout] Tool 'echo' did not finish within 1500 ms");
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task DeferredCompletionThatNeverArrives_EndsAtTheSameDeadline()
        {
            await using var host = await NoneAuthMcpHost.StartAsync(null, "tool-timeout=3000");
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Godot 4.5", onCall: (_, request)
                => Task.FromResult(ResponseCallTool.Processing("queued").SetRequestID(request.RequestID).Pack(request.RequestID)));
            await editor.StartAsync();

            var watch = Stopwatch.StartNew();
            var (status, body) = await client.RestAsync("echo", "{}");
            status.ShouldBe(504);
            body!["error"]!.GetValue<string>().ShouldStartWith("[tool_timeout]");
            // One deadline for the whole call: without it a second full timeout starts after "processing".
            watch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(2900));
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        }

        // ───────────────────────────── plugin reloads ─────────────────────────────

        const string GodotPendingRefusal =
            "Godot is about to reload the project's C# assembly (the built DLL changed), so this call was not run. " +
            "Retry in a few seconds, once the plugin has reconnected.";

        /// <summary>Plays a plugin reload from inside a tool call: drop the connection, then connect a new plugin.</summary>
        sealed class Reload
        {
            readonly ToolCallClient _client;
            Task<FakeEnginePlugin>? _task;

            public Reload(ToolCallClient client) => _client = client;

            public void Start(FakeEnginePlugin plugin, TimeSpan delayBeforeStop)
            {
                _task ??= Task.Run(async () =>
                {
                    await Task.Delay(delayBeforeStop);
                    await plugin.StopAsync();
                    await Task.Delay(300);
                    var reloaded = _client.Plugin("reloaded", "Godot 4.5");
                    await reloaded.StartAsync();
                    return reloaded;
                });
            }

            public async Task<FakeEnginePlugin> PluginAsync()
                => await (_task ?? throw new InvalidOperationException("no reload started")).WaitAsync(TimeSpan.FromSeconds(15));

            public async Task DisposeAsync()
            {
                if (_task == null)
                    return;
                try { await (await _task.WaitAsync(TimeSpan.FromSeconds(15))).DisposeAsync(); }
                catch (Exception) { /* the test already failed on it */ }
            }
        }

        [Fact]
        public async Task CallDeclaredNotExecuted_IsSentAgainOnceThePluginIsBack()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            var reload = new Reload(client);
            await using var editor = client.Plugin("editor", "Godot 4.5", onCall: (plugin, request) =>
            {
                reload.Start(plugin, TimeSpan.FromMilliseconds(100));
                return Task.FromResult(ResponseCallTool.NotExecuted("Reloading; the call was not executed.").SetRequestID(request.RequestID).Pack(request.RequestID));
            });
            await editor.StartAsync();

            try
            {
                var (isError, text) = await client.CallAsync("echo", new { }).WaitAsync(TimeSpan.FromSeconds(30));
                isError.ShouldBeFalse(text);
                text.ShouldBe("reloaded:echo");
                editor.Calls.Count.ShouldBe(1);
                (await reload.PluginAsync()).Calls.Count.ShouldBe(1);
            }
            finally
            {
                await reload.DisposeAsync();
            }
        }

        [Fact]
        public async Task ReloadGateRefusal_IsNotResent_AndSaysWhenThePluginIsBack()
        {
            // Godot's refusal text alone does not prove the call never started, so it is reported, not re-sent.
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            var reload = new Reload(client);
            await using var editor = client.Plugin("editor", "Godot 4.5", onCall: (plugin, request) =>
            {
                reload.Start(plugin, TimeSpan.FromMilliseconds(100));
                return Task.FromResult(FakeEnginePlugin.Error(request, GodotPendingRefusal, ResponseErrorKind.Unavailable));
            });
            await editor.StartAsync();

            try
            {
                var (status, body) = await client.RestAsync("echo", "{}").WaitAsync(TimeSpan.FromSeconds(30));
                status.ShouldBe(503);
                var error = body!["error"]!.GetValue<string>();
                error.ShouldStartWith("[plugin_reloading]");
                error.ShouldContain("is connected again");
                (await reload.PluginAsync()).Calls.ShouldBeEmpty();
            }
            finally
            {
                await reload.DisposeAsync();
            }
        }

        [Fact]
        public async Task CallDroppedAfterSending_IsNotResent_AndSaysThePluginIsBack()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            var reload = new Reload(client);
            await using var editor = client.Plugin("editor", "Godot 4.5", onCall: async (plugin, request) =>
            {
                reload.Start(plugin, TimeSpan.Zero); // the reload starts while the call runs
                await Task.Delay(Timeout.Infinite, plugin.Disposed);
                return FakeEnginePlugin.Success(request, "never");
            });
            await editor.StartAsync();

            try
            {
                var (status, body) = await client.RestAsync("echo", "{}").WaitAsync(TimeSpan.FromSeconds(30));
                status.ShouldBe(502);
                body!["error"]!.GetValue<string>().ShouldStartWith("[plugin_reconnected]");
                editor.Calls.Count.ShouldBe(1);
                (await reload.PluginAsync()).Calls.ShouldBeEmpty(); // never re-run a call that may have run
            }
            finally
            {
                await reload.DisposeAsync();
            }
        }

        [Fact]
        public async Task PluginRefusingUntilTheDeadline_IsReportedAsReloading_NotAsATimeout()
        {
            // The call never ran, so the agent must learn it is safe to retry, not that the engine "may still be running it".
            await using var host = await NoneAuthMcpHost.StartAsync(null, "tool-timeout=3000", "plugin-reconnect-wait-seconds=60");
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Godot 4.5", onCall: (_, request)
                => Task.FromResult(ResponseCallTool.NotExecuted("Reloading; the call was not executed.").SetRequestID(request.RequestID).Pack(request.RequestID)));
            await editor.StartAsync();

            var (status, body) = await client.RestAsync("echo", "{}").WaitAsync(TimeSpan.FromSeconds(30));
            status.ShouldBe(503);
            var error = body!["error"]!.GetValue<string>();
            error.ShouldStartWith("[plugin_reloading]");
            error.ShouldContain("is connected again");
            editor.Calls.Count.ShouldBeGreaterThan(1, "re-sent while the window was open");
        }

        [Fact]
        public async Task LoneSurrogateInARestArgumentName_IsABadRequest()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Godot 4.5");
            await editor.StartAsync();

            var (status, body) = await client.RestAsync("echo", "{\"q\\ud800\":1}");
            status.ShouldBe(400);
            body!["error"]!.GetValue<string>().ShouldStartWith("[invalid_utf16]");
            editor.Calls.ShouldBeEmpty();
        }

        [Fact]
        public async Task CallDroppedAndPluginNeverReturns_SaysSo()
        {
            await using var host = await NoneAuthMcpHost.StartAsync(null, "plugin-reconnect-wait-seconds=2");
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Godot 4.5", onCall: async (plugin, request) =>
            {
                _ = Task.Run(plugin.StopAsync);
                await Task.Delay(Timeout.Infinite, plugin.Disposed);
                return FakeEnginePlugin.Success(request, "never");
            });
            await editor.StartAsync();

            var (status, body) = await client.RestAsync("echo", "{}").WaitAsync(TimeSpan.FromSeconds(30));
            status.ShouldBe(502);
            body!["error"]!.GetValue<string>().ShouldStartWith("[plugin_disconnected]");
        }
    }
}
