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
using com.IvanMurzak.McpPlugin.Common.Model;

namespace com.IvanMurzak.McpPlugin.Common.Hub.Client
{
    /// <summary>
    /// The plugin side of call cancellation. The server sends <see cref="CancelCall"/> (a one-way hub message, no
    /// answer) to the connection running a tool or system tool call whose caller went away, and only to a plugin
    /// whose version handshake listed <see cref="Utils.Consts.MCP.Plugin.Capabilities.CancelCall"/>.
    /// A separate interface: a new member on <see cref="IClientToolHub"/> would break its implementers.
    /// </summary>
    public interface IClientCallCancellation
    {
        /// <summary>Cancels the running call with this request id; an unknown or finished id is ignored.</summary>
        void CancelCall(RequestCancelCall request);
    }
}
