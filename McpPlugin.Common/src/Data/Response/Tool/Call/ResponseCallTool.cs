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
using System.Linq;
using System.Text.Json.Nodes;
using com.IvanMurzak.ReflectorNet.Utils;

namespace com.IvanMurzak.McpPlugin.Common.Model
{
    public partial class ResponseCallTool : IRequestID
    {
        public string RequestID { get; set; } = string.Empty;
        public virtual ResponseStatus Status { get; set; } = ResponseStatus.Error;
        public virtual List<ContentBlock> Content { get; set; } = new List<ContentBlock>();
        public virtual JsonNode? StructuredContent { get; set; } = null;

        public ResponseCallTool() { }
        public ResponseCallTool(ResponseStatus status, List<ContentBlock> content) : this(
            requestId: string.Empty,
            status: status,
            content: content)
        {
            // none
        }
        public ResponseCallTool(string requestId, ResponseStatus status, List<ContentBlock> content)
        {
            RequestID = requestId;
            Status = status;
            Content = content;
        }
        public ResponseCallTool(JsonNode? structuredContent, ResponseStatus status) : this(
            requestId: string.Empty,
            structuredContent: structuredContent,
            status: status)
        {
            // none
        }
        public ResponseCallTool(string requestId, JsonNode? structuredContent, ResponseStatus status)
        {
            RequestID = requestId;
            Status = status;
            StructuredContent = new JsonObject()
            {
                [JsonSchema.Result] = structuredContent
            };
            // MCP backward compatibility: https://modelcontextprotocol.io/specification/2025-06-18/server/tools#structured-content
            Content = new List<ContentBlock>
            {
                new ContentBlock()
                {
                    Type = Consts.ContentType.Text,
                    Text = StructuredContent.ToJsonString(),
                    MimeType = Consts.MimeType.TextJson
                }
            };
        }

        public ResponseCallTool SetRequestID(string requestId)
        {
            RequestID = requestId;
            return this;
        }

        public string? GetMessage() => Content
            ?.FirstOrDefault(item => item.Type == Consts.ContentType.Text && !string.IsNullOrEmpty(item.Text))
            ?.Text;
    }
}
