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
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.DependencyInjection;

namespace com.IvanMurzak.McpPlugin.Server
{
    public static class ExtensionsWebHost
    {
        /// <summary>
        /// Configures Kestrel to listen on the specified port using separate IPv4 and IPv6 bindings.
        /// Avoids dual-stack sockets which cause SocketAddress size mismatch errors on macOS.
        /// Falls back to IPv4-only when <see cref="Socket.OSSupportsIPv6"/> is false.
        /// </summary>
        public static IWebHostBuilder UseKestrelForMcpPlugin(this IWebHostBuilder webHost, int port)
        {
            return webHost
                .UseKestrel(options =>
                {
                    // Bind IPv4 and IPv6 separately instead of using ListenAnyIP
                    // which creates a dual-stack socket that fails on macOS.
                    options.Listen(IPAddress.Any, port);

                    if (Socket.OSSupportsIPv6)
                        options.Listen(IPAddress.IPv6Any, port);
                })
                .ConfigureServices(services =>
                {
                    // Disable dual-stack mode on IPv6 listening sockets.
                    // Prevents System.ArgumentException: "The supplied SocketAddress
                    // is an invalid size for the IPEndPoint end point" on macOS when
                    // accepting IPv4-mapped IPv6 connections.
                    services.Configure<SocketTransportOptions>(socketOptions =>
                    {
                        var defaultFactory = socketOptions.CreateBoundListenSocket;
                        socketOptions.CreateBoundListenSocket = endpoint =>
                        {
                            if (endpoint is IPEndPoint ipEndPoint
                                && ipEndPoint.AddressFamily == AddressFamily.InterNetworkV6)
                            {
                                return CreateBoundIPv6Socket(endpoint);
                            }

                            return defaultFactory != null
                                ? defaultFactory(endpoint)
                                : CreateDefaultBoundListenSocket(endpoint);
                        };
                    });
                });
        }

        private static Socket CreateBoundIPv6Socket(EndPoint endpoint)
        {
            // Create IPv6 socket with DualMode disabled BEFORE binding.
            // DualMode cannot be changed after Bind() on some platforms.
            var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                socket.DualMode = false;
                socket.Bind(endpoint);
                return socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        private static Socket CreateDefaultBoundListenSocket(EndPoint endpoint)
        {
            var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                socket.Bind(endpoint);
                return socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }
}
