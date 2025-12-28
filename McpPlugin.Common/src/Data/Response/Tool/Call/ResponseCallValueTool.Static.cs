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
    public partial class ResponseCallValueTool<T> : ResponseCallTool
    {
        public static new ResponseCallValueTool<T> Error(Exception exception)
            => Error($"[Error] {exception?.Message}\n{exception?.StackTrace}");

        public static new ResponseCallValueTool<T> Error(string? message = null)
            => new ResponseCallValueTool<T>(
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

        public static new ResponseCallValueTool<T> Success(string? message = null)
            => new ResponseCallValueTool<T>(
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

        public static new ResponseCallValueTool<T> SuccessStructured(JsonNode? structuredContent, string? message)
            => new ResponseCallValueTool<T>(
                structuredContent: structuredContent,
                status: ResponseStatus.Success,
                content: new List<ContentBlock>
                {
                    new ContentBlock()
                    {
                        Type = Consts.ContentType.Text,
                        Text = message, // needed for MCP backward compatibility: https://modelcontextprotocol.io/specification/2025-06-18/server/tools#structured-content
                        MimeType = Consts.MimeType.TextJson
                    }
                });

        public static new ResponseCallValueTool<T> Processing(string? message = null)
            => new ResponseCallValueTool<T>(
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
