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
    public interface IConnectionManager : IConnection, IDisposable
    {
        string Endpoint { get; }
        ReadOnlyReactiveProperty<HubConnection?> HubConnection { get; }
        CancellationToken ConnectionCancellationToken { get; }
        /// <summary>
        /// Sets the public connection state to Connected.
        /// Called by the application layer after a successful handshake.
        /// </summary>
        void SetConnected();

        /// <summary>
        /// Fires the <see cref="IConnection.OnAuthorizationRejected"/> event.
        /// Called when the server explicitly rejects the connection due to authorization failure.
        /// </summary>
        void NotifyAuthorizationRejected();

        /// <summary>
        /// Fires when the SignalR transport connection is established (StartAsync succeeded).
        /// This is distinct from application-level Connected state (which requires a successful handshake).
        /// </summary>
        Observable<Unit> OnTransportConnected { get; }

        Task InvokeAsync<TInput>(string methodName, TInput input, CancellationToken cancellationToken = default);
        Task<TResult> InvokeAsync<TInput, TResult>(string methodName, TInput input, CancellationToken cancellationToken = default);
        Task<TResult> InvokeAsync<TResult>(string methodName, CancellationToken cancellationToken = default);
    }
}
