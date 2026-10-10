/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using R3;
using System.Threading;
using System.Threading.Tasks;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// Optional connection lifecycle signal for cancelling credential recovery even when the
    /// transport is already disconnected. Implemented by the built-in connection and plugin.
    /// </summary>
    public interface IConnectionRecovery
    {
        Observable<Unit> OnDisconnectRequested { get; }

        /// <summary>Starts a cancellable connection cycle but returns its first attempt outcome,
        /// leaving transport retries running. A later rejection can start a new credential recovery.</summary>
        Task<bool> ConnectForRecovery(CancellationToken cancellationToken);
    }
}
