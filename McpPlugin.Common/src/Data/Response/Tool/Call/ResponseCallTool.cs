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
        public ResponseErrorKind ErrorKind { get; set; } = ResponseErrorKind.None;
        public int? HttpStatusCode { get; set; }

        public ResponseCallTool() { }
        public ResponseCallTool(ResponseStatus status, List<ContentBlock> content, ResponseErrorKind errorKind = ResponseErrorKind.None, int? httpStatusCode = null) : this(
            requestId: string.Empty,
            status: status,
            content: content,
            errorKind: errorKind,
            httpStatusCode: httpStatusCode)
        {
            // none
        }
        public ResponseCallTool(string requestId, ResponseStatus status, List<ContentBlock> content, ResponseErrorKind errorKind = ResponseErrorKind.None, int? httpStatusCode = null)
        {
            RequestID = requestId;
            Status = status;
            Content = content;
            ErrorKind = errorKind;
            HttpStatusCode = httpStatusCode;
        }
        public ResponseCallTool(JsonNode? structuredContent, ResponseStatus status, ResponseErrorKind errorKind = ResponseErrorKind.None, int? httpStatusCode = null) : this(
            requestId: string.Empty,
            structuredContent: structuredContent,
            status: status,
            errorKind: errorKind,
            httpStatusCode: httpStatusCode)
        {
            // none
        }
        public ResponseCallTool(string requestId, JsonNode? structuredContent, ResponseStatus status, ResponseErrorKind errorKind = ResponseErrorKind.None, int? httpStatusCode = null)
        {
            RequestID = requestId;
            Status = status;
            ErrorKind = errorKind;
            HttpStatusCode = httpStatusCode;
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
