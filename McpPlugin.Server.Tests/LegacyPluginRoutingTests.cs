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
using System.Collections.Generic;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// Plugins built before McpPlugin 8.14.0 (no <c>mcpPluginVersion</c> 8.14.0+ in the handshake) keep server 9.2.x's
    /// newest-wins connection: the editor/runtime roles apply only when BOTH connections are 8.14+.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class LegacyPluginRoutingTests : IDisposable
    {
        const string Editor = "Godot 4.5";
        const string Game = "Godot 4.5 (runtime)";
        const string New = "8.14.0";

        readonly NoAuthMcpStrategy _strategy = new();
        readonly ILogger _logger = new Mock<ILogger>().Object;
        readonly List<string> _disconnected = new();
        readonly List<string> _ids = new();

        void Connect(string id, string environment, string? mcpPluginVersion)
        {
            _ids.Add(id);
            _strategy.OnPluginConnected(typeof(McpServerHub), id, null, _logger, (c, _) => _disconnected.Add(c));
            _strategy.OnPluginHandshake(typeof(McpServerHub), id, environment, mcpPluginVersion, _logger, (c, _) => _disconnected.Add(c));
        }

        string? Routed() => _strategy.ResolveConnectionId(null, 0);

        public void Dispose()
        {
            foreach (var id in _ids)
                _strategy.OnPluginDisconnected(typeof(McpServerHub), id, _logger);
        }

        [Fact]
        public void OldEditorThenOldGame_TheNewestWins()
        {
            Connect("legacy-editor", Editor, null);
            Connect("legacy-game", Game, null);

            _disconnected.ShouldBe(new[] { "legacy-editor" });
            Routed().ShouldBe("legacy-game");
        }

        [Fact]
        public void OldGameThenOldEditor_TheNewestWins()
        {
            Connect("legacy-game2", Game, null);
            Connect("legacy-editor2", Editor, "8.13.1");

            _disconnected.ShouldBe(new[] { "legacy-game2" });
            Routed().ShouldBe("legacy-editor2");
        }

        [Fact]
        public void NewEditorAndNewGame_KeepTheEditor_AndRuntimeToolsGoToTheGame()
        {
            Connect("new-editor", Editor, New);
            Connect("new-game", Game, New);

            _disconnected.ShouldBeEmpty();
            Routed().ShouldBe("new-editor");
            using (PluginRoleRouting.Prefer(ClientRole.Runtime))
                Routed().ShouldBe("new-game");
        }

        [Fact]
        public void NewEditorThenOldGame_FallsBackToNewestWins()
        {
            Connect("mixed-new-editor", Editor, New);
            Connect("mixed-old-game", Game, null);

            _disconnected.ShouldBe(new[] { "mixed-new-editor" });
            Routed().ShouldBe("mixed-old-game");
        }

        [Fact]
        public void OldEditorThenNewGame_FallsBackToNewestWins()
        {
            Connect("mixed-old-editor", Editor, null);
            Connect("mixed-new-game", Game, New);

            _disconnected.ShouldBe(new[] { "mixed-old-editor" });
            using (PluginRoleRouting.Prefer(ClientRole.Runtime))
                Routed().ShouldBe("mixed-new-game");
        }

        [Fact]
        public void OldEditorThenAGameThatDeclaresItsRoleAtConnect_FallsBackToNewestWins()
        {
            // Declaring client_role at connect is an 8.14+ feature: the newcomer is modern, the existing one legacy.
            Connect("declared-old-editor", Editor, null);
            _ids.Add("declared-new-game");
            _strategy.OnPluginConnected(typeof(McpServerHub), "declared-new-game", null, ClientRole.Runtime, _logger,
                (c, _) => _disconnected.Add(c));

            _disconnected.ShouldBe(new[] { "declared-old-editor" });
            ClientUtils.GetAllConnectionIds(typeof(McpServerHub)).ShouldNotContain("declared-old-editor");
        }
    }
}
