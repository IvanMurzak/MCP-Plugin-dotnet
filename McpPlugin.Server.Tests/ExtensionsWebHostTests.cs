/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    [Collection("McpPlugin.Server")]
    public class ExtensionsWebHostTests
    {
        [Fact]
        public void UseKestrelForMcpPlugin_ConfiguresSocketTransportOptions_WithCustomFactory()
        {
            // Arrange
            var port = 19877;
            var builder = WebApplication.CreateBuilder();

            // Act
            builder.WebHost.UseKestrelForMcpPlugin(port);
            using var app = builder.Build();

            // Assert - SocketTransportOptions should have custom CreateBoundListenSocket
            var socketOptions = app.Services.GetRequiredService<IOptions<SocketTransportOptions>>().Value;
            socketOptions.CreateBoundListenSocket.ShouldNotBeNull();
        }

        [Fact]
        public void UseKestrelForMcpPlugin_CustomSocketFactory_DisablesDualModeOnIPv6()
        {
            // Skip on hosts where IPv6 is unavailable
            if (!Socket.OSSupportsIPv6)
                return;

            // Arrange
            var port = 19878;
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrelForMcpPlugin(port);
            using var app = builder.Build();

            var socketOptions = app.Services.GetRequiredService<IOptions<SocketTransportOptions>>().Value;
            var factory = socketOptions.CreateBoundListenSocket;
            factory.ShouldNotBeNull();

            // Act - create an IPv6 socket via the custom factory (port 0 = OS picks available)
            var ipv6Endpoint = new IPEndPoint(IPAddress.IPv6Loopback, 0);
            using var socket = factory(ipv6Endpoint);

            // Assert - DualMode should be disabled for IPv6 sockets
            socket.AddressFamily.ShouldBe(AddressFamily.InterNetworkV6);
            socket.DualMode.ShouldBeFalse();
            socket.IsBound.ShouldBeTrue();
        }

        [Fact]
        public void UseKestrelForMcpPlugin_CustomSocketFactory_DoesNotAffectIPv4Sockets()
        {
            // Arrange
            var port = 19879;
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrelForMcpPlugin(port);
            using var app = builder.Build();

            var socketOptions = app.Services.GetRequiredService<IOptions<SocketTransportOptions>>().Value;
            var factory = socketOptions.CreateBoundListenSocket;
            factory.ShouldNotBeNull();

            // Act - create an IPv4 socket via the custom factory
            var ipv4Endpoint = new IPEndPoint(IPAddress.Loopback, 0);
            using var socket = factory(ipv4Endpoint);

            // Assert - IPv4 socket should not be affected
            socket.AddressFamily.ShouldBe(AddressFamily.InterNetwork);
            socket.IsBound.ShouldBeTrue();
        }

        [Fact]
        public void UseKestrelForMcpPlugin_CustomSocketFactory_IPv4SocketIsStreamTcp()
        {
            // Arrange
            var port = 19880;
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrelForMcpPlugin(port);
            using var app = builder.Build();

            var socketOptions = app.Services.GetRequiredService<IOptions<SocketTransportOptions>>().Value;
            var factory = socketOptions.CreateBoundListenSocket;
            factory.ShouldNotBeNull();

            // Act
            var endpoint = new IPEndPoint(IPAddress.Loopback, 0);
            using var socket = factory(endpoint);

            // Assert - socket should be TCP stream
            socket.SocketType.ShouldBe(SocketType.Stream);
            socket.ProtocolType.ShouldBe(ProtocolType.Tcp);
        }

        [Fact]
        public void UseKestrelForMcpPlugin_CustomSocketFactory_IPv6SocketIsStreamTcp()
        {
            // Skip on hosts where IPv6 is unavailable
            if (!Socket.OSSupportsIPv6)
                return;

            // Arrange
            var port = 19881;
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrelForMcpPlugin(port);
            using var app = builder.Build();

            var socketOptions = app.Services.GetRequiredService<IOptions<SocketTransportOptions>>().Value;
            var factory = socketOptions.CreateBoundListenSocket;
            factory.ShouldNotBeNull();

            // Act
            var endpoint = new IPEndPoint(IPAddress.IPv6Loopback, 0);
            using var socket = factory(endpoint);

            // Assert - IPv6 socket should be TCP stream with DualMode off
            socket.SocketType.ShouldBe(SocketType.Stream);
            socket.ProtocolType.ShouldBe(ProtocolType.Tcp);
            socket.DualMode.ShouldBeFalse();
        }

        [Fact]
        public void UseKestrelForMcpPlugin_ReturnsWebHostBuilder_ForChaining()
        {
            // Arrange
            var port = 19882;
            var builder = WebApplication.CreateBuilder();

            // Act
            var result = builder.WebHost.UseKestrelForMcpPlugin(port);

            // Assert - should return the builder for method chaining
            result.ShouldBeSameAs(builder.WebHost);
        }
    }
}
