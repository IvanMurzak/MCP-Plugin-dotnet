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
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// Wave-6 finding: a game-only tool (Godot <c>game-scene-tree-summary</c>) was answered by the editor while both
    /// were connected. A tool only the runtime has goes to the runtime; everything else still goes to the editor.
    /// Real SignalR connections.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class RuntimeToolRoutingTests
    {
        const string GameTool = "game-scene-tree-summary";

        static ResponseListTool Tool(string name, ToolTarget? target = null) => new ResponseListTool
        {
            Name = name,
            Target = target,
            InputSchema = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}").RootElement.Clone(),
        };

        static async Task<(FakeEnginePlugin Editor, FakeEnginePlugin Game)> StartBoth(ToolCallClient client,
            ResponseListTool[] editorTools, ResponseListTool[] gameTools)
        {
            var editor = client.Plugin("editor", "Godot 4.5.1.stable");
            editor.Tools = editorTools;
            await editor.StartAsync();
            var game = client.Plugin("game", "Godot 4.5.1.stable (runtime)");
            game.Tools = gameTools;
            await game.StartAsync();
            return (editor, game);
        }

        [Fact]
        public async Task RuntimeTargetedTool_ReachesTheGame_WhileTheEditorIsConnected()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            var (editor, game) = await StartBoth(client,
                new[] { Tool("echo") },
                new[] { Tool("echo"), Tool(GameTool, ToolTarget.Runtime) });
            await using var _ = editor;
            await using var __ = game;

            var (isError, text) = await client.CallAsync(GameTool, new { });
            isError.ShouldBeFalse(text);
            text.ShouldBe("game:" + GameTool);

            (await client.CallAsync("echo", new { })).Text.ShouldBe("editor:echo", "a tool both have still goes to the editor");
            editor.Calls.Select(c => c.Name).ShouldBe(new[] { "echo" });
        }

        [Fact]
        public async Task RuntimeTargetedTool_TheEditorAlsoLists_StillReachesTheGame()
        {
            // An editor on an older McpPlugin registers the game tool too (same assembly scan): the runtime's target decides.
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            var (editor, game) = await StartBoth(client,
                new[] { Tool("echo"), Tool(GameTool) },
                new[] { Tool("echo"), Tool(GameTool, ToolTarget.Runtime) });
            await using var _ = editor;
            await using var __ = game;

            (await client.CallAsync(GameTool, new { })).Text.ShouldBe("game:" + GameTool);
            editor.Calls.ShouldBeEmpty();
        }

        [Fact]
        public async Task ToolOnlyTheGameLists_ReachesTheGame_EvenWithoutATarget()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            var (editor, game) = await StartBoth(client,
                new[] { Tool("echo") },
                new[] { Tool("echo"), Tool("game-only") });
            await using var _ = editor;
            await using var __ = game;

            (await client.CallAsync("game-only", new { })).Text.ShouldBe("game:game-only");
            editor.Calls.ShouldBeEmpty();
        }

        [Fact]
        public async Task ToolBothListWithoutARuntimeTarget_GoesToTheEditor()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            var (editor, game) = await StartBoth(client,
                new[] { Tool("echo"), Tool("shared", ToolTarget.Both) },
                new[] { Tool("echo"), Tool("shared") });
            await using var _ = editor;
            await using var __ = game;

            (await client.CallAsync("shared", new { })).Text.ShouldBe("editor:shared");
            game.Calls.ShouldBeEmpty();
        }

        [Fact]
        public async Task ToolsList_IncludesTheGamesOwnTools_WhileTheEditorAnswers()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            var (editor, game) = await StartBoth(client,
                new[] { Tool("echo"), Tool("editor-only", ToolTarget.Editor) },
                new[] { Tool("echo"), Tool(GameTool, ToolTarget.Runtime) });
            await using var _ = editor;
            await using var __ = game;

            var response = await client.RawAsync("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/list\"}");
            var names = response["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToList();
            names.ShouldBe(new[] { "echo", "editor-only", GameTool }, ignoreOrder: true);
        }

        [Fact]
        public async Task RuntimeTargetedTool_DuringAnEditorReload_ReachesTheGameAtOnce()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            var (editor, game) = await StartBoth(client,
                new[] { Tool("echo") },
                new[] { Tool("echo"), Tool(GameTool, ToolTarget.Runtime) });
            await using var _ = editor;
            await using var __ = game;

            await editor.StopAsync();
            var registry = ((IPluginRoleStrategy)host.Services.GetRequiredService<IMcpConnectionStrategy>()).Registry;
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (registry.WithRole(ClientRole.Editor).Count > 0 && DateTime.UtcNow < until)
                await Task.Delay(50);

            var (isError, text) = await client.CallAsync(GameTool, new { }).WaitAsync(TimeSpan.FromSeconds(10));
            isError.ShouldBeFalse(text);
            text.ShouldBe("game:" + GameTool);
        }

        [Fact]
        public void PreferredRuntime_RoutesToTheRuntime_OnlyInsideTheScope()
        {
            var strategy = new NoAuthMcpStrategy();
            strategy.Registry.Add("game", ClientRole.Runtime, null);
            using (PluginRoleRouting.Prefer(ClientRole.Runtime))
                strategy.ResolveConnectionId(null, 0).ShouldBe("game");
            PluginRoleRouting.Preferred.ShouldBeNull();
        }
    }
}
