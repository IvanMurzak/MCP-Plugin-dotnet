/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
└────────────────────────────────────────────────────────────────────────┘
*/

#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    /// <summary>
    /// Unity-MCP#1039: a plugin that is not connected and will not connect (KeepConnected off, or Connect
    /// not called yet, as right after a domain reload) must not try to notify the server about list changes.
    /// That call reached nothing and logged "auto-reconnect disabled" on every editor start.
    /// </summary>
    [Collection("McpPlugin")]
    public class McpPluginNotifyWhileDisconnectedTests
    {
        const string ToolName = "notify-tool";

        [Fact]
        public void ToolVisibilityChange_WhileNotConnected_DoesNotWarn()
        {
            var logger = new CapturingLogger();
            using var plugin = new McpPluginBuilder(new Version())
                .SetConfig(new ConnectionConfig { GenerateSkillFiles = false })
                .AddLogging(builder => builder.AddProvider(new SingleLoggerProvider(logger)))
                .AddTool(ToolName, new MockRunTool(ToolName))
                .Build(new Reflector());

            plugin.KeepConnected.CurrentValue.ShouldBeFalse();

            // The subscription runs synchronously up to its first await, which is where the warning was logged.
            plugin.McpManager.ToolManager!.SetToolVisible(ToolName, false);

            logger.Warnings.ShouldNotContain(w => w.Contains("auto-reconnect disabled"));
        }

        sealed class SingleLoggerProvider : ILoggerProvider
        {
            readonly ILogger _logger;
            public SingleLoggerProvider(ILogger logger) => _logger = logger;
            public ILogger CreateLogger(string categoryName) => _logger;
            public void Dispose() { }
        }

        sealed class MockRunTool : IRunTool
        {
            public MockRunTool(string name) => Name = name;

            public string Name { get; }
            public string? Title => null;
            public string? Description => "Notify test tool";
            public string? SkillDescription => null;
            public string? SkillBody => null;
            public JsonNode? InputSchema => null;
            public JsonNode? OutputSchema => null;
            public bool Visible { get; set; } = true;
            public bool? ReadOnlyHint => null;
            public bool? DestructiveHint => null;
            public bool? IdempotentHint => null;
            public bool? OpenWorldHint => null;
            public int TokenCount => 0;

            public Task<ResponseCallTool> Run(string requestId, IReadOnlyDictionary<string, JsonElement>? namedParameters, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
        }
    }
}
