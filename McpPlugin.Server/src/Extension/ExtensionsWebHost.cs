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
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.DependencyInjection;

namespace com.IvanMurzak.McpPlugin.Server
{
    public static class ExtensionsWebHost
    {
        /// <summary>
        /// Configures Kestrel to listen on the specified port, bound to <b>loopback</b> by default
        /// (decision D8, mcp-authorize b2). This is the 1-argument compatibility overload; it now
        /// defaults to loopback-only just like <see cref="UseKestrelForMcpPlugin(IWebHostBuilder,int,string)"/>
        /// with a null bind.
        /// </summary>
        public static IWebHostBuilder UseKestrelForMcpPlugin(this IWebHostBuilder webHost, int port)
            => webHost.UseKestrelForMcpPlugin(port, bind: null);

        /// <summary>
        /// Resolve the bind argument into the concrete listen addresses. <c>null</c>/empty and the
        /// <c>loopback</c>/<c>localhost</c> aliases yield loopback-only (D8 default). <c>any</c> (or
        /// <c>0.0.0.0</c>/<c>::</c>) yields all-interfaces; any other value is parsed as a specific
        /// <see cref="IPAddress"/>. IPv6 addresses are included only when the OS supports IPv6.
        /// </summary>
        public static IReadOnlyList<IPAddress> ResolveBindAddresses(string? bind)
        {
            var value = bind?.Trim();

            if (string.IsNullOrEmpty(value)
                || string.Equals(value, "loopback", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return WithOptionalIPv6(IPAddress.Loopback, IPAddress.IPv6Loopback);
            }

            if (string.Equals(value, "any", StringComparison.OrdinalIgnoreCase))
                return WithOptionalIPv6(IPAddress.Any, IPAddress.IPv6Any);

            if (!IPAddress.TryParse(value, out var parsed))
                throw new ArgumentException($"Invalid --bind value '{bind}'. Use 'loopback', 'any', or a specific IP address.", nameof(bind));

            if (parsed.AddressFamily == AddressFamily.InterNetworkV6 && !Socket.OSSupportsIPv6)
                throw new ArgumentException($"--bind '{bind}' requests IPv6 but the OS does not support it.", nameof(bind));

            return new[] { parsed };
        }

        private static IReadOnlyList<IPAddress> WithOptionalIPv6(IPAddress v4, IPAddress v6)
        {
            var list = new List<IPAddress> { v4 };
            if (Socket.OSSupportsIPv6)
                list.Add(v6);
            return list;
        }

        /// <summary>
        /// Configures Kestrel to listen on the specified port using separate IPv4 and IPv6 bindings
        /// resolved from <paramref name="bind"/> (default loopback, D8). Avoids dual-stack sockets
        /// which cause SocketAddress size mismatch errors on macOS. Falls back to IPv4-only when
        /// <see cref="Socket.OSSupportsIPv6"/> is false.
        /// </summary>
        public static IWebHostBuilder UseKestrelForMcpPlugin(this IWebHostBuilder webHost, int port, string? bind)
        {
            if (webHost == null) throw new ArgumentNullException(nameof(webHost));
            if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be between 0 and 65535.");

            var addresses = ResolveBindAddresses(bind);

            return webHost
                .UseKestrel(options =>
                {
                    // Bind each resolved address separately instead of using ListenAnyIP
                    // which creates a dual-stack socket that fails on macOS.
                    foreach (var address in addresses)
                        options.Listen(address, port);
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
                            if (endpoint is IPEndPoint ipEndPoint && ipEndPoint.AddressFamily == AddressFamily.InterNetworkV6)
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
            if (endpoint is not IPEndPoint)
                throw new NotSupportedException($"Endpoint type '{endpoint.GetType().Name}' is not supported by the default socket factory.");

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
