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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using R3;

namespace com.IvanMurzak.McpPlugin
{
    public interface IConnection : IDisposable
    {
        ReadOnlyReactiveProperty<bool> KeepConnected { get; }
        ReadOnlyReactiveProperty<HubConnectionState> ConnectionState { get; }

        /// <summary>
        /// Fires when the server repeatedly rejects the connection immediately after handshake,
        /// typically due to an invalid or revoked authorization token.
        /// Subscribers should clear cached credentials and prompt the user to re-authorize.
        /// </summary>
        Observable<Unit> OnAuthorizationRejected { get; }

        Task<bool> Connect(CancellationToken cancellationToken = default);
        Task Disconnect(CancellationToken cancellationToken = default);
        /// <summary>
        /// Immediately disconnects without waiting for async cleanup.
        /// Use this during Unity assembly reload or other critical shutdown scenarios.
        /// </summary>
        void DisconnectImmediate();

        /// <summary>
        /// Bounded-block until the most recent <see cref="DisconnectImmediate"/>'s background transport
        /// disposal completes, so a caller on a reload/AssemblyLoadContext-unload thread can ensure the
        /// transport's threads/handles (HttpClient pool timer, WebSocket receive loop) are released before
        /// the unload — addressing godotengine/godot#78513. Returns true if the disposal completed within
        /// <paramref name="timeout"/> (or nothing was pending); false on timeout/error.
        /// </summary>
        bool WaitForImmediateTeardown(TimeSpan timeout);
    }
}
