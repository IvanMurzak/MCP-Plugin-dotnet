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
using System.Reflection;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection
{
    /// <summary>
    /// Regression guard for mcp-authorize b7 folded fix 1: the legacy client-side shared-token surface
    /// (<c>MCP_PLUGIN_TOKEN</c> / <c>mcp-plugin-token</c> / <c>ConnectionConfig.Token</c>) was removed —
    /// nothing reads the old shared token, and the credential-provider replacement is in place.
    /// </summary>
    public sealed class LegacySharedTokenRemovalTests
    {
        [Fact]
        public void ConnectionConfig_HasNoLegacyTokenSurface()
        {
            var type = typeof(ConnectionConfig);
            type.GetProperty("Token").ShouldBeNull("ConnectionConfig.Token (the static shared token) must be removed");
            type.GetMethod("GetTokenFromArgsOrEnv", BindingFlags.Public | BindingFlags.Static)
                .ShouldBeNull("ConnectionConfig.GetTokenFromArgsOrEnv must be removed");
        }

        [Fact]
        public void ConnectionConfig_HasCredentialProviderReplacement()
        {
            var type = typeof(ConnectionConfig);
            var credentialProvider = type.GetProperty("CredentialProvider");
            credentialProvider.ShouldNotBeNull("the credential-provider callback replaces the static token");
            credentialProvider!.PropertyType.ShouldBe(typeof(Func<Task<string?>>));

            type.GetProperty("InstanceMetadata").ShouldNotBeNull("the b7 instance-metadata handshake surface must exist");
        }

        [Fact]
        public void PluginTokenConsts_AreRemoved()
        {
            // The plugin-side shared-token arg/env constants are gone (nothing can parse them anymore).
            typeof(Consts.MCP.Plugin.Args).GetField("McpPluginToken", BindingFlags.Public | BindingFlags.Static)
                .ShouldBeNull("Consts.MCP.Plugin.Args.McpPluginToken must be removed");
            typeof(Consts.MCP.Plugin.Env).GetField("McpPluginToken", BindingFlags.Public | BindingFlags.Static)
                .ShouldBeNull("Consts.MCP.Plugin.Env.McpPluginToken must be removed");
        }

        [Fact]
        public void BuildFromArgsOrEnv_DoesNotResurrectAToken_FromMcpPluginTokenEnv()
        {
            var previous = Environment.GetEnvironmentVariable("MCP_PLUGIN_TOKEN");
            try
            {
                Environment.SetEnvironmentVariable("MCP_PLUGIN_TOKEN", "leaked-shared-token");

                var config = ConnectionConfig.BuildFromArgsOrEnv(Array.Empty<string>());

                // No credential is read from the legacy env var — the plugin now presents an
                // auto-refreshed account JWT via CredentialProvider, which is wired by the host, not here.
                config.CredentialProvider.ShouldBeNull();
            }
            finally
            {
                Environment.SetEnvironmentVariable("MCP_PLUGIN_TOKEN", previous);
            }
        }
    }
}
