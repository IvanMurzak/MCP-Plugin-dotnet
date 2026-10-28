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
using System.Text.Json;
using com.IvanMurzak.McpPlugin.Common;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection
{
    /// <summary>
    /// The plugin declares its <see cref="ConnectionConfig.ClientRole"/> on the hub connection, so the server
    /// keeps a game (runtime) connection beside the editor instead of letting it replace the editor.
    /// </summary>
    public sealed class ClientRoleHubUrlTests
    {
        [Fact]
        public void DefaultRole_IsEditor_AndIsSentAsAQueryParameter()
        {
            var config = new ConnectionConfig { Host = "http://localhost:8080" };

            config.ClientRole.ShouldBe(ClientRole.Editor);
            HubConnectionProvider.BuildHubUrl(config, Consts.Hub.RemoteApp)
                .ShouldBe("http://localhost:8080" + Consts.Hub.RemoteApp + "?client_role=editor");
        }

        [Fact]
        public void RuntimeRole_IsSentAfterTheInstanceMetadata()
        {
            var config = new ConnectionConfig
            {
                Host = "http://localhost:8080",
                ClientRole = ClientRole.Runtime,
                InstanceMetadata = new ConnectionInstanceMetadata("inst-1", "unity", "MyGame", "hash", "PC-1"),
            };

            var url = new Uri(HubConnectionProvider.BuildHubUrl(config, Consts.Hub.RemoteApp));

            url.AbsolutePath.ShouldBe(Consts.Hub.RemoteApp);
            url.Query.ShouldStartWith("?instance_id=inst-1&");
            url.Query.ShouldEndWith("&client_role=runtime");
        }

        [Fact]
        public void ClientRole_IsNotPartOfTheSavedConfig()
        {
            // A host fact (editor vs game build), not a user setting: never written to the on-disk config.
            var json = JsonSerializer.Serialize(new ConnectionConfig { ClientRole = ClientRole.Runtime });
            json.ShouldNotContain("ClientRole");
        }

        [Theory]
        [InlineData(ClientRole.Editor, "editor")]
        [InlineData(ClientRole.Runtime, "runtime")]
        public void WireValue_RoundTripsThroughTheServerParser(ClientRole role, string wire)
        {
            ClientRoles.ToWire(role).ShouldBe(wire);
            ClientRoles.Parse(wire).ShouldBe(role);
        }
    }
}
