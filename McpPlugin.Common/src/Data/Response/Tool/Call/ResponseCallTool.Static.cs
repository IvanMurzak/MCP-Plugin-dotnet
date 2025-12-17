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
using System.Linq;
using System.Text.Json.Nodes;

namespace com.IvanMurzak.McpPlugin.Common.Model
{
    public partial class ResponseCallTool : IRequestID
    {
        public static ResponseCallTool Error(Exception exception)
            => Error($"[Error] {exception?.Message}\n{exception?.StackTrace}");

        public static ResponseCallTool Error(string? message = null)
            => new ResponseCallTool(
                status: ResponseStatus.Error,
                content: new List<ContentBlock>
                {
                    new ContentBlock()
                    {
                        Type = "text",
                        Text = message,
                        MimeType = Consts.MimeType.TextPlain
                    }
                });

        public static ResponseCallTool Success(string? message = null)
            => new ResponseCallTool(
                status: ResponseStatus.Success,
                content: new List<ContentBlock>
                {
                    new ContentBlock()
                    {
                        Type = "text",
                        Text = message,
                        MimeType = Consts.MimeType.TextPlain
                    }
                });

        public static ResponseCallTool SuccessStructured(JsonNode? structuredContent, string? message)
            => new ResponseCallTool(
                structuredContent: structuredContent,
                status: ResponseStatus.Success,
                content: new List<ContentBlock>
                {
                    new ContentBlock()
                    {
                        Type = "text",
                        Text = message, // needed for MCP backward compatibility: https://modelcontextprotocol.io/specification/2025-06-18/server/tools#structured-content
                        MimeType = Consts.MimeType.TextJson
                    }
                });


        public static ResponseCallTool ErrorStructured(JsonNode? structuredContent, string? message)
            => new ResponseCallTool(
                structuredContent: structuredContent,
                status: ResponseStatus.Error,
                content: new List<ContentBlock>
                {
                    new ContentBlock()
                    {
                        Type = "text",
                        Text = message, // needed for MCP backward compatibility: https://modelcontextprotocol.io/specification/2025-06-18/server/tools#structured-content
                        MimeType = Consts.MimeType.TextJson
                    }
                });

        public static ResponseCallTool Processing(string? message = null)
            => new ResponseCallTool(
                status: ResponseStatus.Processing,
                content: new List<ContentBlock>
                {
                    new ContentBlock()
                    {
                        Type = "text",
                        Text = message,
                        MimeType = Consts.MimeType.TextPlain
                    }
                });

        /// <summary>
        /// Creates a successful response containing image content.
        /// </summary>
        /// <param name="data">Raw image bytes</param>
        /// <param name="mimeType">MIME type (e.g., "image/png", "image/jpeg"). Use Consts.MimeType constants.</param>
        /// <param name="message">Optional text message to include alongside the image</param>
        public static ResponseCallTool Image(byte[] data, string mimeType, string? message = null)
        {
            var content = new List<ContentBlock>
            {
                new ContentBlock()
                {
                    Type = "image",
                    Data = Convert.ToBase64String(data),
                    MimeType = mimeType
                }
            };

            if (!string.IsNullOrEmpty(message))
            {
                content.Insert(0, new ContentBlock()
                {
                    Type = "text",
                    Text = message,
                    MimeType = Consts.MimeType.TextPlain
                });
            }

            return new ResponseCallTool(
                status: ResponseStatus.Success,
                content: content);
        }

        /// <summary>
        /// Creates a successful response containing audio content.
        /// </summary>
        /// <param name="data">Raw audio bytes</param>
        /// <param name="mimeType">MIME type (e.g., "audio/wav", "audio/mpeg"). Use Consts.MimeType constants.</param>
        /// <param name="message">Optional text message to include alongside the audio</param>
        public static ResponseCallTool Audio(byte[] data, string mimeType, string? message = null)
        {
            var content = new List<ContentBlock>
            {
                new ContentBlock()
                {
                    Type = "audio",
                    Data = Convert.ToBase64String(data),
                    MimeType = mimeType
                }
            };

            if (!string.IsNullOrEmpty(message))
            {
                content.Insert(0, new ContentBlock()
                {
                    Type = "text",
                    Text = message,
                    MimeType = Consts.MimeType.TextPlain
                });
            }

            return new ResponseCallTool(
                status: ResponseStatus.Success,
                content: content);
        }

        /// <summary>
        /// Creates a successful response with multiple content blocks.
        /// Use this when returning mixed content (e.g., text + images + audio).
        /// </summary>
        /// <param name="contentBlocks">Array of content blocks to include</param>
        public static ResponseCallTool WithContent(params ContentBlock[] contentBlocks)
            => new ResponseCallTool(
                status: ResponseStatus.Success,
                content: contentBlocks.ToList());

        /// <summary>
        /// Creates a response with specified status and multiple content blocks.
        /// </summary>
        /// <param name="status">Response status</param>
        /// <param name="contentBlocks">Array of content blocks to include</param>
        public static ResponseCallTool WithContent(ResponseStatus status, params ContentBlock[] contentBlocks)
            => new ResponseCallTool(
                status: status,
                content: contentBlocks.ToList());
    }
}
