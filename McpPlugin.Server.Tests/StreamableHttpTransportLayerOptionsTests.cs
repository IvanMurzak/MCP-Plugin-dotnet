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
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    // Regression guard for the production memory leak (issue #119): the streamableHttp transport
    // MUST configure the SDK's session-eviction knobs (IdleTimeout + MaxIdleSessionCount) from
    // DataArguments. Without MaxIdleSessionCount the SDK default (10,000) let zombie sessions —
    // each pinning a grown SseEventWriter buffer — accumulate to multi-GB.
    [Collection("McpPlugin.Server")]
    public class StreamableHttpTransportLayerOptionsTests
    {
        static HttpServerTransportOptions ConfigureAndResolve(DataArguments dataArguments)
        {
            var services = new ServiceCollection();
            var mcpServerBuilder = services.AddMcpServer();

            new StreamableHttpTransportLayer().ConfigureTransport(mcpServerBuilder, dataArguments);

            using var provider = services.BuildServiceProvider();
            return provider.GetRequiredService<IOptions<HttpServerTransportOptions>>().Value;
        }

        [Fact]
        public void ConfigureTransport_WiresIdleTimeoutAndMaxIdleSessionCount_FromExplicitArgs()
        {
            var args = new DataArguments(new[] { "idle-timeout-seconds=123", "max-idle-session-count=77" });

            var options = ConfigureAndResolve(args);

            options.IdleTimeout.ShouldBe(TimeSpan.FromSeconds(123));
            options.MaxIdleSessionCount.ShouldBe(77);
        }

        [Fact]
        public void ConfigureTransport_WiresDefaults_FromDataArguments()
        {
            var args = new DataArguments(Array.Empty<string>());

            var options = ConfigureAndResolve(args);

            // Assert the options mirror the resolved DataArguments (robust to ambient env vars).
            options.IdleTimeout.ShouldBe(TimeSpan.FromSeconds(args.IdleTimeoutSeconds));
            options.MaxIdleSessionCount.ShouldBe(args.MaxIdleSessionCount);
        }

        [Fact]
        public void ConfigureTransport_KeepsStatefulPerSessionLifecycle()
        {
            // Stateful + per-session execution context are load-bearing for the session lifecycle
            // (StartAsync/StopAsync via RunSessionHandler). Guard against accidental regression.
            var options = ConfigureAndResolve(new DataArguments(Array.Empty<string>()));

            options.Stateless.ShouldBeFalse();
            options.PerSessionExecutionContext.ShouldBeTrue();
        }
    }
}
