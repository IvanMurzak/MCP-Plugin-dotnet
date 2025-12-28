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
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace com.IvanMurzak.McpPlugin.Common.Model
{
    public partial class ResponseCallTool : IRequestID
    {
        public static ResponseCallTool Error(Exception exception)
        {
            return Error($"[Error] {exception?.Message}\n{exception?.StackTrace}");
        }

        public static ResponseCallTool Error(string? message = null)
        {
            return new ResponseCallTool(
                status: ResponseStatus.Error,
                content: new List<ContentBlock>
                {
                    new ContentBlock()
                    {
                        Type = Consts.ContentType.Text,
                        Text = message,
                        MimeType = Consts.MimeType.TextPlain
                    }
                });
        }

        public static ResponseCallTool Success(string? message = null)
        {
            return new ResponseCallTool(
                status: ResponseStatus.Success,
                content: new List<ContentBlock>
                {
                    new ContentBlock()
                    {
                        Type = Consts.ContentType.Text,
                        Text = message,
                        MimeType = Consts.MimeType.TextPlain
                    }
                });
        }

        public static ResponseCallTool SuccessStructured(JsonNode? structuredContent)
        {
            return new ResponseCallTool(
                structuredContent: structuredContent,
                status: ResponseStatus.Success);
        }

        public static ResponseCallTool ErrorStructured(JsonNode? structuredContent)
        {
            return new ResponseCallTool(
                structuredContent: structuredContent,
                status: ResponseStatus.Error);
        }

        public static ResponseCallTool Processing(string? message = null)
        {
            return new ResponseCallTool(
                status: ResponseStatus.Processing,
                content: new List<ContentBlock>
                {
                    new ContentBlock()
                    {
                        Type = Consts.ContentType.Text,
                        Text = message,
                        MimeType = Consts.MimeType.TextPlain
                    }
                });
        }
    }
}
