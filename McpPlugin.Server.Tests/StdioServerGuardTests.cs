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
using System.Net;
using System.Net.Sockets;
using com.IvanMurzak.McpPlugin.Server.Transport;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// stdio same-project concurrency contract (mcp-authorize b5, design 03 Flow D):
    /// the FIRST spawn owns the derived per-project port; a LATER spawn for the same ProjectIdentity
    /// detects the live server on the EXACT port and must NOT self-start a second server — it exits
    /// with the actionable message (fallback). No port probing: an in-use port surfaces an explicit
    /// Owned result on that exact port, never a silent bind on a neighbouring port.
    /// </summary>
    public class StdioServerGuardTests
    {
        /// <summary>Bind an ephemeral loopback port, capture it, release it — a port very likely free.</summary>
        static int GetEphemeralPort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        [Fact]
        public void CheckExactPort_PortOwnedByLiveServer_ReturnsOwnedWithActionableMessage()
        {
            // Arrange — a live "first spawn" owns the derived port.
            var owner = new TcpListener(IPAddress.Loopback, 0);
            owner.Start();
            var port = ((IPEndPoint)owner.LocalEndpoint).Port;
            try
            {
                // Act — a "later spawn" checks the SAME port.
                var result = StdioServerGuard.CheckExactPort(port);

                // Assert — detected as owned; the second spawn must not bind.
                result.IsOwned.ShouldBeTrue();
                result.Status.ShouldBe(StdioPortStatus.Owned);
                result.Message.ShouldNotBeNull();
                result.Message!.ShouldContain(port.ToString());
                result.Message.ShouldContain("http"); // actionable: prefer the http config for multi-session
            }
            finally
            {
                owner.Stop();
            }
        }

        [Fact]
        public void CheckExactPort_ReportsExactPort_NeverProbesANeighbour()
        {
            // A held port must be reported as Owned on that EXACT port — the guard never silently
            // falls back to port+1 (no probing).
            var owner = new TcpListener(IPAddress.Loopback, 0);
            owner.Start();
            var port = ((IPEndPoint)owner.LocalEndpoint).Port;
            try
            {
                var result = StdioServerGuard.CheckExactPort(port);

                result.Port.ShouldBe(port);
                result.IsOwned.ShouldBeTrue();
            }
            finally
            {
                owner.Stop();
            }
        }

        [Fact]
        public void CheckExactPort_FreePort_ReturnsAvailable()
        {
            // Arrange — a port no server owns.
            var port = GetEphemeralPort();

            // Act
            var result = StdioServerGuard.CheckExactPort(port);

            // Assert — free: this spawn may own it and start the server.
            result.Status.ShouldBe(StdioPortStatus.Available);
            result.IsOwned.ShouldBeFalse();
            result.Message.ShouldBeNull();
            result.Port.ShouldBe(port);
        }

        [Fact]
        public void CheckExactPort_ReleasedPort_TransitionsOwnedToAvailable()
        {
            // Own the port, observe Owned; release it, observe Available — the check tracks the
            // exact port's live ownership rather than probing.
            var owner = new TcpListener(IPAddress.Loopback, 0);
            owner.Start();
            var port = ((IPEndPoint)owner.LocalEndpoint).Port;

            StdioServerGuard.CheckExactPort(port).IsOwned.ShouldBeTrue();

            owner.Stop();

            StdioServerGuard.CheckExactPort(port).Status.ShouldBe(StdioPortStatus.Available);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(70000)]
        public void CheckExactPort_PortOutOfRange_Throws(int port)
        {
            Should.Throw<ArgumentOutOfRangeException>(() => StdioServerGuard.CheckExactPort(port));
        }
    }
}
