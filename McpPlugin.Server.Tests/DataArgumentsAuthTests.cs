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
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>Parsing of the new OAuth resource-server args/env (mcp-authorize b2).</summary>
    [Collection("McpPlugin.Server")]
    public class DataArgumentsAuthTests
    {
        [Fact]
        public void AuthOAuth_Args_Parsed()
        {
            var args = new DataArguments(new[]
            {
                "auth=oauth",
                "auth-issuer=https://ai-game.dev",
                "public-url=http://localhost:23471",
                "bind=any"
            });

            args.Authorization.ShouldBe(Consts.MCP.Server.AuthOption.oauth);
            args.AuthIssuer.ShouldBe("https://ai-game.dev");
            args.PublicUrl.ShouldBe("http://localhost:23471");
            args.Bind.ShouldBe("any");
        }

        [Fact]
        public void LegacyAuthorizationRequired_StillParsed()
        {
            var args = new DataArguments(new[] { "authorization=required" });
            args.Authorization.ShouldBe(Consts.MCP.Server.AuthOption.required);
        }

        [Fact]
        public void AuthNone_Parsed()
        {
            var args = new DataArguments(new[] { "auth=none" });
            args.Authorization.ShouldBe(Consts.MCP.Server.AuthOption.none);
        }

        [Fact]
        public void NewAuthArg_OverridesLegacyAuthorizationArg()
        {
            // Both supplied: the target-state --auth wins over legacy --authorization.
            var args = new DataArguments(new[] { "authorization=required", "auth=oauth" });
            args.Authorization.ShouldBe(Consts.MCP.Server.AuthOption.oauth);
        }

        [Fact]
        public void BindDefault_IsNull()
        {
            var args = new DataArguments(Array.Empty<string>());
            args.Bind.ShouldBeNull();
        }

        [Fact]
        public void Env_MCP_AUTH_Parsed()
        {
            try
            {
                Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.Auth, "oauth");
                Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.AuthIssuer, "https://as.example");
                Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.PublicUrl, "http://localhost:5555");
                Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.Bind, "any");

                var args = new DataArguments(Array.Empty<string>());

                args.Authorization.ShouldBe(Consts.MCP.Server.AuthOption.oauth);
                args.AuthIssuer.ShouldBe("https://as.example");
                args.PublicUrl.ShouldBe("http://localhost:5555");
                args.Bind.ShouldBe("any");
            }
            finally
            {
                Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.Auth, null);
                Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.AuthIssuer, null);
                Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.PublicUrl, null);
                Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.Bind, null);
            }
        }
    }
}
