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
using System.Linq;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// MCP C# SDK 2.x clients probe the <c>2026-07-28</c> protocol revision first (no <c>initialize</c>,
    /// no <c>Mcp-Session-Id</c>). This server is deliberately STATEFUL (session lifecycle, replace-by-identity,
    /// unsolicited list_changed notifications), so it refuses that probe and the client must fall back to the
    /// <c>initialize</c> handshake and obtain a session. Proves the round trip with the real SDK client over
    /// the production wiring, in token mode (the session-owner claim path).
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class Sdk2ProtocolNegotiationTests
    {
        const string Secret = "sdk2-negotiation-shared-secret";

        [Fact]
        public async Task Sdk2Client_FallsBackToInitialize_AndGetsAStatefulSession()
        {
            var dataArguments = new DataArguments(new[] { "client-transport=streamableHttp", "auth=token", $"token={Secret}" });

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Services
                .WithMcpServer(dataArguments)
                .WithMcpPluginServer(dataArguments);

            await using var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            app.UseMcpPluginServer(dataArguments);
            await app.StartAsync();
            try
            {
                var baseUrl = app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()!.Addresses.First();

                var transport = new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri(new Uri(baseUrl), "/mcp"),
                    TransportMode = HttpTransportMode.StreamableHttp,
                    AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {Secret}" },
                });

                await using var client = await McpClient.CreateAsync(transport);

                client.NegotiatedProtocolVersion.ShouldNotBeNull();
                client.NegotiatedProtocolVersion.ShouldNotBe("2026-07-28",
                    "a stateful server must push a 2.x client down to the initialize handshake");
                client.SessionId.ShouldNotBeNullOrEmpty("the fallback must yield a stateful session");

                // A request after the handshake must ride the same session (no plugin connected => empty list).
                var tools = await client.ListToolsAsync();
                tools.ShouldNotBeNull();
            }
            finally
            {
                await app.StopAsync();
            }
        }
    }
}
