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
            structuredContent: null,
            status: status,
            content: content)
        {
            // none
        }
        public ResponseCallTool(JsonNode? structuredContent, ResponseStatus status, List<ContentBlock> content) : this(
            requestId: string.Empty,
            structuredContent: structuredContent,
            status: status,
            content: content)
        {
            // none
        }
        public ResponseCallTool(string requestId, JsonNode? structuredContent, ResponseStatus status, List<ContentBlock> content)
        {
            RequestID = requestId;
            Status = status;
            Content = content;
            StructuredContent = new JsonObject()
            {
                [JsonSchema.Result] = structuredContent
            };

            // Update unstructured content text to match structured content format (wrap in "result")
            if (structuredContent != null)
            {
                var textContent = Content?.FirstOrDefault(c => c.Type == Consts.ContentType.Text);
                if (textContent != null)
                {
                    textContent.Text = StructuredContent?.ToJsonString();
                }
            }
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
