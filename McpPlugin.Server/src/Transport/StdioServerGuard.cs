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

namespace com.IvanMurzak.McpPlugin.Server.Transport
{
    /// <summary>Outcome of the stdio same-project port-ownership check.</summary>
    public enum StdioPortStatus
    {
        /// <summary>The derived per-project port is free — this spawn may own it and start the server.</summary>
        Available,

        /// <summary>
        /// The derived per-project port is already owned by a live server for this ProjectIdentity —
        /// this spawn MUST NOT bind. It either proxies to the live server or exits with the actionable
        /// <see cref="StdioContentionResult.Message"/> (the design-03 Flow D fallback).
        /// </summary>
        Owned
    }

    /// <summary>Result of <see cref="StdioServerGuard.CheckExactPort"/>.</summary>
    public sealed class StdioContentionResult
    {
        public StdioPortStatus Status { get; }

        /// <summary>The exact port that was checked (never a probed alternative).</summary>
        public int Port { get; }

        /// <summary>Actionable, human-readable message — non-null only when <see cref="Status"/> is <see cref="StdioPortStatus.Owned"/>.</summary>
        public string? Message { get; }

        public bool IsOwned => Status == StdioPortStatus.Owned;

        internal StdioContentionResult(StdioPortStatus status, int port, string? message)
        {
            Status = status;
            Port = port;
            Message = message;
        }
    }

    /// <summary>
    /// stdio same-project concurrency contract (mcp-authorize b5, design 03 Flow D).
    /// <para>
    /// The FIRST stdio spawn for a project owns that project's deterministic ProjectIdentity port
    /// (design 02 D15). A LATER spawn for the SAME ProjectIdentity must detect the live server on
    /// that <b>exact</b> port and never self-start a second server on an already-owned port — it
    /// proxies to the live server or exits with an actionable message. There is <b>no port probing</b>:
    /// only the derived port is tested; an already-in-use port surfaces an explicit
    /// <see cref="StdioPortStatus.Owned"/> result (or, for any non-"address-in-use" bind failure, the
    /// underlying <see cref="SocketException"/> is rethrown — the host must fail loudly, never silently
    /// retry on a neighbouring port).
    /// </para>
    /// </summary>
    public static class StdioServerGuard
    {
        /// <summary>The exact actionable message a later same-project stdio spawn surfaces on contention.</summary>
        public static string OwnedMessage(int port)
            => $"An MCP server for this project is already running on port {port}. "
             + "A stdio spawn never self-starts a second server on an already-owned port — "
             + "prefer the http config for multi-session use in one project.";

        /// <summary>
        /// Probes ONLY the exact <paramref name="port"/> on the same loopback address the Kestrel
        /// hub listener binds (resolved from <paramref name="bind"/>; loopback by default, D8).
        /// Returns <see cref="StdioPortStatus.Owned"/> (with <see cref="StdioContentionResult.Message"/>)
        /// when the port is already in use, or <see cref="StdioPortStatus.Available"/> when it is free.
        /// Never probes any other port. Any bind failure other than "address already in use" is rethrown.
        /// </summary>
        public static StdioContentionResult CheckExactPort(int port, string? bind = null)
        {
            if (port < 0 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be between 0 and 65535.");

            // Resolve the SAME bind address the Kestrel listener uses (loopback by default, D8), so the
            // ownership check tests exactly the socket the server would bind — not a different interface.
            var addresses = ExtensionsWebHost.ResolveBindAddresses(bind);
            var address = addresses.Count > 0 ? addresses[0] : IPAddress.Loopback;

            var listener = new TcpListener(address, port);
            try
            {
                // Exact-port bind attempt — never a probe of port+1.
                listener.Start();
                return new StdioContentionResult(StdioPortStatus.Available, port, null);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                // The derived port is owned by a live server for this ProjectIdentity.
                return new StdioContentionResult(StdioPortStatus.Owned, port, OwnedMessage(port));
            }
            finally
            {
                try { listener.Stop(); }
                catch { /* best-effort release of the probe socket */ }
            }
        }
    }
}
