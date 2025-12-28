/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace com.IvanMurzak.McpPlugin.Common.Model
{
    public partial class ResponseCallValueTool<T> : ResponseCallTool
    {
        public ResponseCallValueTool() : base() { }
        public ResponseCallValueTool(ResponseStatus status, List<ContentBlock> content) : base(
            requestId: string.Empty,
            status: status,
            content: content)
        {
            // none
        }
        public ResponseCallValueTool(string requestId, ResponseStatus status, List<ContentBlock> content) : base(
            requestId: requestId,
            status: status,
            content: content)
        {
            // none
        }
        public ResponseCallValueTool(JsonNode? structuredContent, ResponseStatus status) : base(
            requestId: string.Empty,
            structuredContent: structuredContent,
            status: status)
        {
            // none
        }
        public ResponseCallValueTool(string requestId, JsonNode? structuredContent, ResponseStatus status) : base(
            requestId: requestId,
            structuredContent: structuredContent,
            status: status)
        {
            // none
        }

        public new ResponseCallValueTool<T> SetRequestID(string requestId)
        {
            RequestID = requestId;
            return this;
        }
    }
}
