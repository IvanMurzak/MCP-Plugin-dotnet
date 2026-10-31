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
using com.IvanMurzak.McpPlugin.Common.Model;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// Thrown by an <c>[AiTool]</c> method (a reflected <see cref="RunTool"/>) when its host cannot serve the call right
    /// now (still starting, compiling, reloading), not because of what the call asked for. The call fails with
    /// <see cref="ResponseErrorKind.Unavailable"/>: HTTP 503 on the server's direct endpoint, an ordinary error result
    /// with the message on the MCP channel. (A hand-written <see cref="IRunTool"/> returns
    /// <c>ResponseCallTool.Error(message, ResponseErrorKind.Unavailable)</c> or <see cref="ResponseCallTool.NotExecuted"/> instead.)
    /// </summary>
    public class ToolUnavailableException : Exception
    {
        /// <param name="message">What the agent sees, e.g. what to wait for before retrying.</param>
        /// <param name="notExecuted">See <see cref="NotExecuted"/>.</param>
        public ToolUnavailableException(string message, bool notExecuted = false) : base(message)
        {
            NotExecuted = notExecuted;
        }

        /// <summary>
        /// True only when the tool did nothing before it threw (no change of any kind), so running the call again is
        /// safe. Such a failure is answered as <see cref="ResponseCallTool.NotExecuted"/>, and a server with the call
        /// guard (McpPlugin.Server 8.14.0+) sends the call again once the plugin is back. False (the default) is a
        /// plain <see cref="ResponseErrorKind.Unavailable"/> failure the server never sends again.
        /// </summary>
        public bool NotExecuted { get; }
    }
}
