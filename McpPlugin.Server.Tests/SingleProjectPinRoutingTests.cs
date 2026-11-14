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
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// DEV-42: a single-project server (<c>auth=none</c>, <c>token</c>) routes a session pinned to a project
    /// (<c>/mcp/p/&lt;pin&gt;</c>, <c>/p/&lt;pin&gt;/api/…</c>) only to that project's plugin, over real SignalR
    /// connections. Another project's editor never answers it, however late it connects; an unpinned session, and a
    /// plugin that names no project, are routed as before.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class SingleProjectPinRoutingTests
    {
        const string HashA = "aabbccdd00112233445566778899aabbccddeeff00112233445566778899aabb";
        const string HashB = "1122334400112233445566778899aabbccddeeff00112233445566778899aabb";
        const string PinA = "aabbccdd";
        const string PinB = "11223344";
        const string NotConnected = "The engine for this project is not connected";
        const string OtherProject = "serves another project";

        // Well under the ~10 s retry ladder: a pinned call with another project's editor present has nothing to wait for.
        static readonly TimeSpan Fast = TimeSpan.FromSeconds(3);

        [Fact]
        public async Task TwoProjectsEditors_EachPinnedSessionReachesOnlyItsOwnProject()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var pinnedA = new ToolCallClient(host, PinA);
            using var pinnedB = new ToolCallClient(host, PinB);
            await using var editorA = pinnedA.Plugin("A", "Godot 4.5", projectPathHash: HashA);
            await editorA.StartAsync();

            (await pinnedA.CallAsync("echo", new { })).Text.ShouldBe("A:echo");
            var watch = Stopwatch.StartNew();
            var (isError, text) = await pinnedB.CallAsync("echo", new { });
            watch.Elapsed.ShouldBeLessThan(Fast, "nothing to wait for: the only editor serves another project");
            isError.ShouldBeTrue(text);
            text.ShouldStartWith(NotConnected);
            text.ShouldContain(OtherProject);
            (await ToolNames(pinnedB)).ShouldNotContain("echo", "another project's tools are never listed to a pinned session");
            editorA.Calls.Count.ShouldBe(1);

            // B's editor takes the one editor slot; now B's session reaches B and A's session reaches nothing.
            await using var editorB = pinnedB.Plugin("B", "Godot 4.5", projectPathHash: HashB);
            await editorB.StartAsync();
            await editorA.ForceDisconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

            (await pinnedB.CallAsync("echo", new { })).Text.ShouldBe("B:echo");
            (isError, text) = await pinnedA.CallAsync("echo", new { });
            isError.ShouldBeTrue(text);
            text.ShouldStartWith(NotConnected);
            editorB.Calls.Count.ShouldBe(1);
            editorA.Calls.Count.ShouldBe(1);
        }

        [Fact]
        public async Task LateJoiningEditorOfAnotherProject_NeverAnswersAPinnedSession()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var pinnedA = new ToolCallClient(host, PinA);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            // No editor at all: the probe hears "no editor for this project" (what the App reads as pin routing).
            var (status, error) = await PingAsync(http, host, PinA);
            status.ShouldBe(HttpStatusCode.ServiceUnavailable);
            error.ShouldStartWith(NotConnected);
            error.ShouldNotContain(OtherProject);

            // Another project's editor opens later: the pinned probe and calls still never reach it.
            await using var editorB = pinnedA.Plugin("B", "Godot 4.5", projectPathHash: HashB);
            await editorB.StartAsync();
            (status, error) = await PingAsync(http, host, PinA);
            status.ShouldBe(HttpStatusCode.ServiceUnavailable);
            error.ShouldStartWith(NotConnected);
            error.ShouldContain(OtherProject);
            var (isError, text) = await pinnedA.CallAsync("echo", new { });
            isError.ShouldBeTrue(text);
            text.ShouldStartWith(NotConnected);
            editorB.Calls.ShouldBeEmpty();

            // Its own editor opens: it is served.
            await using var editorA = pinnedA.Plugin("A", "Godot 4.5", projectPathHash: HashA);
            await editorA.StartAsync();
            (await pinnedA.CallAsync("echo", new { })).Text.ShouldBe("A:echo");
            editorB.Calls.ShouldBeEmpty();
        }

        [Fact]
        public async Task UnpinnedSession_IsRoutedAsBefore()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var editorB = client.Plugin("B", "Godot 4.5", projectPathHash: HashB);
            await editorB.StartAsync();

            (await client.CallAsync("echo", new { })).Text.ShouldBe("B:echo");
            (await ToolNames(client)).ShouldContain("echo");
        }

        [Fact]
        public async Task PluginThatNamesNoProject_StillServesAPinnedSession()
        {
            // A plugin from before the handshake metadata: nothing shows it serves another project.
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var pinnedA = new ToolCallClient(host, PinA);
            await using var editor = pinnedA.Plugin("legacy", "Godot 4.5");
            await editor.StartAsync();

            (await pinnedA.CallAsync("echo", new { })).Text.ShouldBe("legacy:echo");
        }

        [Fact]
        public async Task RememberedListsOfAnOfflineEditor_AreListedOnlyToItsOwnProject()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var pinnedA = new ToolCallClient(host, PinA);
            using var pinnedB = new ToolCallClient(host, PinB);
            await using var editorA = pinnedA.Plugin("A", "Godot 4.5", projectPathHash: HashA);
            await editorA.StartAsync();
            (await ToolNames(pinnedA)).ShouldContain("echo");

            // A reloads (drops its connection): its session keeps the remembered list, another project's never gets it.
            await editorA.StopAsync();
            await WaitUntil(() => RoleRegistry(host).WithRole(ClientRole.Editor).Count == 0);
            (await ToolNames(pinnedA)).ShouldContain("echo");
            (await ToolNames(pinnedB)).ShouldNotContain("echo");
        }

        [Fact]
        public void Strategy_PinnedResolution_IgnoresOtherProjects_AndKeepsUnpinnedRouting()
        {
            var strategy = new NoAuthMcpStrategy();
            strategy.Registry.SetProject("game-b", new PluginProject(HashB));
            strategy.Registry.Add("game-b", ClientRole.Runtime, null);

            strategy.ResolveConnectionId(null, 0).ShouldBe("game-b");
            McpSessionTokenContext.CurrentProjectPin = PinA;
            try
            {
                strategy.ResolveConnectionId(null, 0).ShouldBeNull();
                strategy.PinnedNotConnectedMessage().ShouldNotBeNull().ShouldContain(OtherProject);
                McpSessionTokenContext.CurrentProjectPin = PinB;
                strategy.ResolveConnectionId(null, 0).ShouldBe("game-b");
            }
            finally
            {
                McpSessionTokenContext.CurrentProjectPin = null;
            }
            strategy.PinnedNotConnectedMessage().ShouldBeNull();
        }

        static PluginConnectionRegistry RoleRegistry(NoneAuthMcpHost host)
            => ((IPluginRoleStrategy)host.Services.GetRequiredService<IMcpConnectionStrategy>()).Registry;

        static async Task WaitUntil(Func<bool> condition)
        {
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!condition() && DateTime.UtcNow < until)
                await Task.Delay(50);
            condition().ShouldBeTrue();
        }

        static async Task<string[]> ToolNames(ToolCallClient client)
        {
            var response = await client.RawAsync("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/list\"}");
            return response["result"]?["tools"]?.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToArray() ?? Array.Empty<string>();
        }

        static async Task<(HttpStatusCode Status, string Error)> PingAsync(HttpClient http, NoneAuthMcpHost host, string pin)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{host.BaseUrl}/p/{pin}/api/system-tools/ping")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            using var response = await http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);
            var error = json.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? string.Empty : string.Empty;
            return (response.StatusCode, error);
        }
    }
}
