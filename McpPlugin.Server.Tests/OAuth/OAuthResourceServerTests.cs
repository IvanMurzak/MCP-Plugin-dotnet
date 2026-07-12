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
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Auth.OAuth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests.OAuth
{
    [Collection("McpPlugin.Server")]
    public class OAuthResourceServerTests
    {
        [Fact]
        public void Config_DerivesEndpoints()
        {
            var config = new OAuthResourceServerConfig("https://ai-game.dev/", "http://localhost:23471");
            config.Issuer.ShouldBe("https://ai-game.dev");
            config.JwksUri.ShouldBe("https://ai-game.dev/.well-known/jwks.json");
            config.IntrospectionEndpoint.ShouldBe("https://ai-game.dev/oauth/introspect");
            config.ClockSkew.ShouldBe(TimeSpan.FromMinutes(5));
        }

        [Fact]
        public void Config_RejectsMissingIssuerOrResource()
        {
            Should.Throw<ArgumentException>(() => new OAuthResourceServerConfig("", "http://localhost:1"));
            Should.Throw<ArgumentException>(() => new OAuthResourceServerConfig("https://x", ""));
        }

        [Fact]
        public void ProtectedResourceMetadataUrl_PathLessResource_Appends()
        {
            var config = new OAuthResourceServerConfig("https://as.example", "http://localhost:23471");
            config.ProtectedResourceMetadataUrl()
                .ShouldBe("http://localhost:23471/.well-known/oauth-protected-resource");
        }

        [Fact]
        public void ProtectedResourceMetadataUrl_ResourceWithPath_RootInserts()
        {
            var config = new OAuthResourceServerConfig("https://ai-game.dev", "https://ai-game.dev/mcp");
            config.ProtectedResourceMetadataUrl()
                .ShouldBe("https://ai-game.dev/.well-known/oauth-protected-resource/mcp");
        }

        [Fact]
        public void ProtectedResourceMetadata_Content()
        {
            var config = new OAuthResourceServerConfig("https://ai-game.dev", "https://ai-game.dev/mcp");
            var doc = OAuthProtectedResourceMetadata.Build(config);

            doc["resource"].ShouldBe("https://ai-game.dev/mcp");
            ((string[])doc["authorization_servers"]).ShouldBe(new[] { "https://ai-game.dev" });
            ((string[])doc["scopes_supported"]).ShouldBe(new[] { "mcp:agent" });
            ((string[])doc["bearer_methods_supported"]).ShouldBe(new[] { "header" });
        }

        [Fact]
        public void StrategyFactory_CreatesOAuthStrategy()
        {
            // mcp-authorize b3: oauth mode is now the account+instance pairing plane (AccountMcpStrategy),
            // superseding the b2 interim OAuthMcpStrategy.
            var strategy = new McpStrategyFactory().Create(Consts.MCP.Server.AuthOption.oauth);
            strategy.ShouldBeOfType<AccountMcpStrategy>();
            strategy.AuthOption.ShouldBe(Consts.MCP.Server.AuthOption.oauth);
        }

        [Fact]
        public void OAuthStrategy_Validate_RequiresIssuerAndPublicUrl()
        {
            var strategy = new AccountMcpStrategy();

            Should.Throw<ArgumentException>(() =>
                strategy.Validate(new DataArguments(new[] { "auth=oauth" })));

            // Both present → no throw.
            strategy.Validate(new DataArguments(new[]
            {
                "auth=oauth", "auth-issuer=https://as.example", "public-url=http://localhost:1"
            }));
        }

        [Fact]
        public void OAuthStrategy_ConfigureAuthentication_SetsOAuthMode()
        {
            var strategy = new AccountMcpStrategy();
            var options = new TokenAuthenticationOptions();
            strategy.ConfigureAuthentication(options, new DataArguments(new[]
            {
                "auth=oauth", "auth-issuer=https://as.example", "public-url=http://localhost:1"
            }));

            options.OAuthMode.ShouldBeTrue();
        }
    }
}
