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
    /// Thrown by a tool when its host cannot serve the call right now (still starting, compiling, reloading), not
    /// because of what the call asked for. The call fails with <see cref="ResponseErrorKind.Unavailable"/>: HTTP 503 on
    /// the server's direct endpoint, an ordinary error result with the message on the MCP channel.
    /// Keep the phrase "this call was not run" out of the message: a server reads it as a reload-gate refusal.
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
        /// safe. Such a failure is answered as <see cref="ResponseCallTool.NotExecuted"/>: a server with the call guard
        /// (McpPlugin.Server 8.14.0+, for plugins on 8.14.0+) sends the call again, after a reconnect or every couple of
        /// seconds while the plugin stays connected, within its reconnect wait; then it answers 503 with a "plugin
        /// reloading" error that quotes this message. False (the default) is a plain
        /// <see cref="ResponseErrorKind.Unavailable"/> failure with no status, which no server sends again.
        /// </summary>
        public bool NotExecuted { get; }

        /// <summary>The HTTP status a failure from <paramref name="exception"/> carries: the not-executed 503, or none.</summary>
        internal static int? StatusCodeOf(Exception exception)
            => exception is ToolUnavailableException { NotExecuted: true } ? ResponseCallTool.NotExecutedStatusCode : (int?)null;
    }
}
