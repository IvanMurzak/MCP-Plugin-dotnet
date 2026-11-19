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
using System.Text.Json.Serialization;

namespace com.IvanMurzak.McpPlugin.Common.Model
{
    /// <summary>
    /// The server's request to stop a tool or system tool call it sent earlier, because the caller went away (the
    /// HTTP request was aborted, or the MCP client sent <c>notifications/cancelled</c>). Best effort: the plugin
    /// stops the call only where that is safe, and nobody waits for its answer any more.
    /// </summary>
    public class RequestCancelCall
    {
        /// <summary>The <see cref="RequestCallTool.RequestID"/> of the call to stop.</summary>
        [JsonPropertyName("requestId")]
        public string RequestID { get; set; } = string.Empty;

        public RequestCancelCall() { }
        public RequestCancelCall(string requestId) => RequestID = requestId;

        public override string ToString() => $"RequestCancelCall {{ RequestID = {RequestID} }}";
    }
}
