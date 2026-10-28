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
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// An editor and a game (runtime) plugin connected at once, over real SignalR connections (wave-4/5
    /// finding: the game evicted the editor, and every editor tool call then reached the game).
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class PluginRoleRoutingTests
    {
        static readonly TimeSpan EvictionWindow = TimeSpan.FromMilliseconds(750);

        [Fact]
        public async Task GameFromHandshake_DoesNotEvictEditor_AndToolCallsReachTheEditor()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Godot 4.5.1.stable");
            await editor.StartAsync();
            await using var game = client.Plugin("game", "Godot 4.5.1.stable (runtime)");
            await game.StartAsync();

            (await editor.WasEvictedWithin(EvictionWindow)).ShouldBeFalse("the game connection disconnected the editor");
            var (isError, text) = await client.CallAsync("echo", new { });
            isError.ShouldBeFalse(text);
            text.ShouldBe("editor:echo");
            game.Calls.ShouldBeEmpty();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task DeclaredRuntimeRole_DoesNotEvictEditor(bool roleInHeader)
        {
            // Unity's handshake environment is the same in the editor and a player build: only the declared role tells them apart.
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "6000.0.40f1", declaredRole: "editor", roleInHeader: roleInHeader);
            await editor.StartAsync();
            await using var game = client.Plugin("game", "6000.0.40f1", declaredRole: "runtime", roleInHeader: roleInHeader);
            await game.StartAsync();

            (await editor.WasEvictedWithin(EvictionWindow)).ShouldBeFalse();
            (await client.CallAsync("echo", new { })).Text.ShouldBe("editor:echo");
        }

        [Fact]
        public async Task DeclaredRole_WinsOverTheHandshakeEnvironment()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Godot 4.5 (runtime)", declaredRole: "editor");
            await editor.StartAsync();
            await using var game = client.Plugin("game", "Godot 4.5 (runtime)", declaredRole: "runtime");
            await game.StartAsync();

            (await editor.WasEvictedWithin(EvictionWindow)).ShouldBeFalse();
            (await client.CallAsync("echo", new { })).Text.ShouldBe("editor:echo");
        }

        [Fact]
        public async Task RefusedHandshake_DoesNotReplaceTheEditor()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Godot 4.5");
            await editor.StartAsync();
            await using var stale = client.Plugin("stale", "Godot 4.5", apiVersion: "0.0.0-incompatible");
            await Should.ThrowAsync<InvalidOperationException>(stale.StartAsync);

            (await editor.WasEvictedWithin(EvictionWindow)).ShouldBeFalse();
            (await client.CallAsync("echo", new { })).Text.ShouldBe("editor:echo");
        }

        [Fact]
        public async Task NewerEditor_StillReplacesOlderEditor()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var first = client.Plugin("first", "Godot 4.5");
            await first.StartAsync();
            await using var second = client.Plugin("second", "Godot 4.5");
            await second.StartAsync();

            (await first.ForceDisconnected.Task.WaitAsync(TimeSpan.FromSeconds(5))).ShouldNotBeNull().ShouldContain("newer editor connection");
            (await client.CallAsync("echo", new { })).Text.ShouldBe("second:echo");
        }

        [Fact]
        public async Task NewerGame_ReplacesOlderGame_NotTheEditor()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Godot 4.5");
            await editor.StartAsync();
            await using var game1 = client.Plugin("game1", "Godot 4.5 (runtime)");
            await game1.StartAsync();
            await using var game2 = client.Plugin("game2", "Godot 4.5 (runtime)");
            await game2.StartAsync();

            (await game1.ForceDisconnected.Task.WaitAsync(TimeSpan.FromSeconds(5))).ShouldNotBeNull().ShouldContain("newer runtime connection");
            (await editor.WasEvictedWithin(EvictionWindow)).ShouldBeFalse();
            (await client.CallAsync("echo", new { })).Text.ShouldBe("editor:echo");
            game2.Calls.ShouldBeEmpty();
        }

        [Fact]
        public async Task GameAlone_ServesToolCalls()
        {
            // A standalone game build with no editor: the runtime is the only target.
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var game = client.Plugin("game", "Godot 4.5 (runtime)");
            await game.StartAsync();

            var (isError, text) = await client.CallAsync("echo", new { });
            isError.ShouldBeFalse(text);
            text.ShouldBe("game:echo");
        }

        [Fact]
        public async Task CallsDuringEditorReconnect_WaitForTheEditor_NotTheGame()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Godot 4.5");
            await editor.StartAsync();
            await using var game = client.Plugin("game", "Godot 4.5 (runtime)");
            await game.StartAsync();
            (await client.CallAsync("echo", new { })).Text.ShouldBe("editor:echo");

            await editor.StopAsync(); // an editor assembly reload
            var registry = Registry(host);
            (await PollUntil(() => registry.WithRole(ClientRole.Editor).Count == 0)).ShouldBeTrue();

            var call = client.CallAsync("echo", new { });
            await Task.Delay(500);
            await using var reloaded = client.Plugin("reloaded", "Godot 4.5");
            await reloaded.StartAsync();

            var (isError, text) = await call.WaitAsync(TimeSpan.FromSeconds(30));
            isError.ShouldBeFalse(text);
            text.ShouldBe("reloaded:echo");
            game.Calls.ShouldBeEmpty();
        }

        [Fact]
        public async Task GameConnectingDuringAnEditorReload_DoesNotTakeTheEditorsCalls()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Godot 4.5");
            await editor.StartAsync();
            (await client.CallAsync("echo", new { })).Text.ShouldBe("editor:echo");

            await editor.StopAsync();
            var registry = Registry(host);
            (await PollUntil(() => registry.WithRole(ClientRole.Editor).Count == 0)).ShouldBeTrue();

            var call = client.CallAsync("echo", new { });
            await using var game = client.Plugin("game", "Godot 4.5 (runtime)"); // its role is unknown until its handshake
            await game.StartAsync();
            await Task.Delay(500);
            await using var reloaded = client.Plugin("reloaded", "Godot 4.5");
            await reloaded.StartAsync();

            (await call.WaitAsync(TimeSpan.FromSeconds(30))).Text.ShouldBe("reloaded:echo");
            game.Calls.ShouldBeEmpty();
        }

        [Fact]
        public async Task TokenMode_GameDoesNotEvictEditor()
        {
            // The same rule in auth=token: one editor and one runtime connection, each replacing only its own role.
            const string secret = "role-test-secret";
            await using var host = await NoneAuthMcpHost.StartAsync(null, "auth=token", "token=" + secret);
            using var client = new ToolCallClient(host);
            var strategy = host.Services.GetRequiredService<IMcpConnectionStrategy>().ShouldBeOfType<LocalTokenMcpStrategy>();

            await using var editor = client.Plugin("editor", "6000.0.40f1", declaredRole: "editor", token: secret);
            await editor.StartAsync();
            await using var game = client.Plugin("game", "6000.0.40f1", declaredRole: "runtime", token: secret);
            await game.StartAsync();
            await using var game2 = client.Plugin("game2", "Godot 4.5 (runtime)", token: secret);
            await game2.StartAsync();

            (await game.ForceDisconnected.Task.WaitAsync(TimeSpan.FromSeconds(5))).ShouldNotBeNull().ShouldContain("auth=token");
            (await editor.WasEvictedWithin(EvictionWindow)).ShouldBeFalse("a game must not evict the editor in token mode");
            strategy.Registry.WithRole(ClientRole.Editor).ShouldBe(new[] { editor.ConnectionId! });
            strategy.Registry.WithRole(ClientRole.Runtime).ShouldBe(new[] { game2.ConnectionId! });
            strategy.ResolveConnectionId(null, 0).ShouldBe(editor.ConnectionId);
        }

        static PluginConnectionRegistry Registry(NoneAuthMcpHost host)
            => ((IPluginRoleStrategy)host.Services.GetRequiredService<IMcpConnectionStrategy>()).Registry;

        static async Task<bool> PollUntil(Func<bool> condition)
        {
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < until)
            {
                if (condition())
                    return true;
                await Task.Delay(50);
            }
            return condition();
        }
    }

    /// <summary>The role bookkeeping on its own.</summary>
    public sealed class PluginConnectionRegistryTests
    {
        [Fact]
        public void EditorLeftWithin_UsesTheRegistryClock()
        {
            var now = DateTimeOffset.UtcNow;
            var registry = new PluginConnectionRegistry(() => now);
            registry.Add("game", ClientRole.Runtime, null);
            registry.Remove("game", out _);
            registry.EditorLeftWithin(TimeSpan.FromSeconds(60)).ShouldBeFalse("a game leaving is not an editor leaving");

            registry.Add("editor", ClientRole.Editor, null);
            registry.Remove("editor", out _);
            now += TimeSpan.FromSeconds(59);
            registry.EditorLeftWithin(TimeSpan.FromSeconds(60)).ShouldBeTrue();
            now += TimeSpan.FromSeconds(2);
            registry.EditorLeftWithin(TimeSpan.FromSeconds(60)).ShouldBeFalse();
        }

        [Fact]
        public void OlderConnectionClassifiedLate_ReplacesItself_NotTheNewerOne()
        {
            var registry = new PluginConnectionRegistry();
            registry.Add("old", null, null);
            registry.Add("new", ClientRole.Editor, null).ShouldBeEmpty();
            registry.TryClassify("old", ClientRole.Editor, out _, out var replaced).ShouldBeTrue();
            replaced.ShouldBe(new[] { "old" });
            registry.WithRole(ClientRole.Editor).ShouldBe(new[] { "new" });
        }

        [Fact]
        public void DeclaredRole_IsNotReplacedByTheHandshake()
        {
            var registry = new PluginConnectionRegistry();
            registry.Add("editor", ClientRole.Editor, "t");
            registry.TryClassify("editor", ClientRole.Runtime, out _, out _).ShouldBeFalse();
            registry.GetRole("editor").ShouldBe(ClientRole.Editor);
            registry.GetToken("editor").ShouldBe("t");
        }

        [Theory]
        [InlineData("Godot 4.5.1.stable (runtime)", ClientRole.Runtime)]
        [InlineData("Godot 4.5.1.stable", ClientRole.Editor)]
        [InlineData("6000.0.40f1", ClientRole.Editor)]
        [InlineData("", ClientRole.Editor)]
        [InlineData(null, ClientRole.Editor)]
        public void FromEnvironment(string? environment, ClientRole expected)
            => ClientRoles.FromEnvironment(environment).ShouldBe(expected);

        [Theory]
        [InlineData("editor", ClientRole.Editor)]
        [InlineData("Runtime", ClientRole.Runtime)]
        [InlineData("game", ClientRole.Runtime)]
        [InlineData("player", ClientRole.Runtime)]
        [InlineData("server", null)]
        [InlineData(null, null)]
        public void Parse(string? value, ClientRole? expected)
            => ClientRoles.Parse(value).ShouldBe(expected);
    }
}
