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
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server.Tools;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// The 8.14.0 handshake field <c>mcpPluginVersion</c> and the per-connection decision it drives: only a
    /// plugin on McpPlugin 8.14.0+ is held to its <c>required</c> list; older plugins keep their behaviour.
    /// </summary>
    public sealed class PluginArgumentPolicyTests
    {
        [Theory]
        [InlineData("8.14.0", true)]
        [InlineData("8.14.1", true)]
        [InlineData("8.15.0", true)]
        [InlineData("9.0.0", true)]
        [InlineData("8.14.0-dev.71fa5c1", true)]
        [InlineData("8.14.0+build.7", true)]
        [InlineData("8.14", true)]
        [InlineData("8.13.1", false)]
        [InlineData("8.13.0", false)]
        [InlineData("7.5.2", false)]
        [InlineData("1.6.0", false)] // a host version (Unity-MCP) is not an McpPlugin version, but is below anyway
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData("v8.14.0", false)]
        [InlineData("8.x", false)]
        [InlineData("8", false)]
        public void EnforcesRequired_FromTheReportedMcpPluginVersion(string? version, bool expected)
            => PluginArgumentPolicy.EnforcesRequired(version).ShouldBe(expected);

        [Fact]
        public void Connections_AreRecordedAndForgotten_UnknownIsLenient()
        {
            PluginArgumentPolicy.IsEnforced("conn-unknown").ShouldBeFalse();
            PluginArgumentPolicy.IsEnforced(null).ShouldBeFalse();

            PluginArgumentPolicy.Record("conn-new", "8.14.0");
            PluginArgumentPolicy.Record("conn-old", null);
            PluginArgumentPolicy.IsEnforced("conn-new").ShouldBeTrue();
            PluginArgumentPolicy.IsEnforced("conn-old").ShouldBeFalse();

            PluginArgumentPolicy.Forget("conn-new");
            PluginArgumentPolicy.IsEnforced("conn-new").ShouldBeFalse();
            PluginArgumentPolicy.Forget("conn-old");
        }

        /// <summary>The handshake DTO of McpPlugin 8.13 (and the 9.2.17 server built on it): no <c>mcpPluginVersion</c>.</summary>
        sealed class RequestVersionHandshake813
        {
            [JsonPropertyName("requestId")] public string RequestID { get; set; } = string.Empty;
            [JsonPropertyName("apiVersion")] public string ApiVersion { get; set; } = string.Empty;
            [JsonPropertyName("pluginVersion")] public string PluginVersion { get; set; } = string.Empty;
            [JsonPropertyName("environment")] public string Environment { get; set; } = string.Empty;
        }

        [Fact]
        public void A814Handshake_DeserializesIntoThe813Shape_TheExtraFieldIsIgnored()
        {
            // SignalR's JSON hub protocol uses System.Text.Json, which ignores unknown members by default.
            var json = JsonSerializer.Serialize(new RequestVersionHandshake
            {
                RequestID = "r1",
                ApiVersion = "2.0.0",
                PluginVersion = "1.6.0",
                Environment = "Unity 6000.3",
                McpPluginVersion = "8.14.0",
            });
            json.ShouldContain("\"mcpPluginVersion\":\"8.14.0\"");

            var old = JsonSerializer.Deserialize<RequestVersionHandshake813>(json).ShouldNotBeNull();
            old.RequestID.ShouldBe("r1");
            old.ApiVersion.ShouldBe("2.0.0");
            old.PluginVersion.ShouldBe("1.6.0");
            old.Environment.ShouldBe("Unity 6000.3");

            // And the other way: an 8.13 plugin's handshake reads as "no McpPlugin version" on 8.14.
            var fromOld = JsonSerializer.Deserialize<RequestVersionHandshake>(
                JsonSerializer.Serialize(new RequestVersionHandshake813 { RequestID = "r2", ApiVersion = "2.0.0" })).ShouldNotBeNull();
            fromOld.McpPluginVersion.ShouldBeNull();
        }
    }
}
