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
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using com.IvanMurzak.ReflectorNet;
using Shouldly;
using Xunit;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// DEV-132: when the caller of a direct tool call goes away (the App aborts the HTTP request on Stop / Ctrl-C),
    /// the server asks the plugin to stop the call, and the running tool's cancellation token fires. A server
    /// timeout does not cancel the tool, and a plugin that did not list the capability is never sent the cancel.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class ToolCallCancelPropagationTests
    {
        static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

        /// <summary>A tool that reports its start, then waits for its token (or <paramref name="maxRun"/>).</summary>
        sealed class WaitingTool
        {
            public readonly TaskCompletionSource<bool> Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            /// <summary>True when the token fired, false when the tool ran to its end.</summary>
            public readonly TaskCompletionSource<bool> Ended = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public ProxyTool Create(string name, TimeSpan maxRun)
                => new ProxyTool(name, null, "Waits until cancelled.", null, null, null, null, null, null, null, null,
                    async (requestId, arguments, cancellationToken) =>
                    {
                        var ambient = ToolCallCancellation.Token;
                        Started.TrySetResult(true);
                        try
                        {
                            await Task.Delay(maxRun, cancellationToken);
                            Ended.TrySetResult(false);
                            return ResponseCallTool.Success("ran to its end");
                        }
                        catch (OperationCanceledException)
                        {
                            Ended.TrySetResult(ambient.IsCancellationRequested);
                            throw;
                        }
                    });
        }

        static async Task<IMcpPlugin> ConnectPluginAsync(NoneAuthMcpHost host, string projectRoot, string name, ProxyTool tool)
        {
            var plugin = new McpPluginBuilder(new Version { Api = Consts.ApiVersion, Plugin = "0.0.0-test", Environment = "test-engine" })
                .WithConfig(config =>
                {
                    config.Host = host.BaseUrl;
                    config.ProjectRootPath = projectRoot;
                    config.GenerateSkillFiles = false;
                })
                .AddTool(name, tool)
                .Build(new Reflector());
            await plugin.Connect(CancellationToken.None);
            var until = DateTime.UtcNow + Wait;
            while (plugin.VersionHandshakeStatus?.Compatible != true && DateTime.UtcNow < until)
                await Task.Delay(50);
            plugin.VersionHandshakeStatus?.Compatible.ShouldBe(true);
            return plugin;
        }

        static string NewProjectRoot()
            => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mcpplugin-cancel-" + Guid.NewGuid().ToString("N"))).FullName;

        static HttpRequestMessage ToolCall(NoneAuthMcpHost host, string name)
            => new HttpRequestMessage(HttpMethod.Post, $"{host.BaseUrl}/api/tools/{name}")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };

        [Fact]
        public async Task AbortedHttpCall_CancelsTheRunningTool()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            var projectRoot = NewProjectRoot();
            var waiting = new WaitingTool();
            using var plugin = await ConnectPluginAsync(host, projectRoot, "slow", waiting.Create("slow", TimeSpan.FromMinutes(2)));
            try
            {
                using var http = new HttpClient();
                using var abort = new CancellationTokenSource();
                var call = http.SendAsync(ToolCall(host, "slow"), abort.Token);
                await waiting.Started.Task.WaitAsync(Wait);

                abort.Cancel(); // the App's Stop: the HTTP request is aborted
                await Should.ThrowAsync<OperationCanceledException>(() => call);

                (await waiting.Ended.Task.WaitAsync(Wait)).ShouldBeTrue("the tool's token and ToolCallCancellation.Token fire");
            }
            finally
            {
                await plugin.Disconnect();
                try { Directory.Delete(projectRoot, recursive: true); } catch (IOException) { }
            }
        }

        [Fact]
        public async Task ServerTimeout_DoesNotCancelTheTool()
        {
            await using var host = await NoneAuthMcpHost.StartAsync(null, "tool-timeout=1000");
            var projectRoot = NewProjectRoot();
            var waiting = new WaitingTool();
            using var plugin = await ConnectPluginAsync(host, projectRoot, "slow", waiting.Create("slow", TimeSpan.FromSeconds(3)));
            try
            {
                using var http = new HttpClient();
                using var response = await http.SendAsync(ToolCall(host, "slow")).WaitAsync(Wait);
                response.IsSuccessStatusCode.ShouldBeFalse("the server gave up after its tool timeout");

                (await waiting.Ended.Task.WaitAsync(Wait)).ShouldBeFalse("only a caller that went away cancels the tool");
            }
            finally
            {
                await plugin.Disconnect();
                try { Directory.Delete(projectRoot, recursive: true); } catch (IOException) { }
            }
        }

        [Fact]
        public void Capability_IsRecordedPerConnection_AndForgotten()
        {
            PluginCallCancellation.Record("conn-new", new[] { "other", Consts.MCP.Plugin.Capabilities.CancelCall });
            PluginCallCancellation.Record("conn-old", null);
            PluginCallCancellation.Record("conn-none", new[] { "other" });
            try
            {
                PluginCallCancellation.IsSupported("conn-new").ShouldBeTrue();
                PluginCallCancellation.IsSupported("conn-old").ShouldBeFalse("an older plugin lists no capabilities");
                PluginCallCancellation.IsSupported("conn-none").ShouldBeFalse();
                PluginCallCancellation.IsSupported(null).ShouldBeFalse("a call that was never sent has no connection");

                PluginCallCancellation.Record("conn-new", null); // a re-handshake without it
                PluginCallCancellation.IsSupported("conn-new").ShouldBeFalse();
                PluginCallCancellation.Record("conn-new", new[] { Consts.MCP.Plugin.Capabilities.CancelCall });
                PluginCallCancellation.Forget("conn-new");
                PluginCallCancellation.IsSupported("conn-new").ShouldBeFalse();
            }
            finally
            {
                PluginCallCancellation.Forget("conn-new");
                PluginCallCancellation.Forget("conn-old");
                PluginCallCancellation.Forget("conn-none");
            }
        }
    }
}
