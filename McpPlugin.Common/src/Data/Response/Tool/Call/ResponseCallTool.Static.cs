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
        public static ResponseCallTool Error(Exception exception, ResponseErrorKind errorKind = ResponseErrorKind.Internal, int? httpStatusCode = null)
        {
            return Error($"[Error] {exception?.Message}\n{exception?.StackTrace}", errorKind, httpStatusCode);
        }

        public static ResponseCallTool Error(string? message = null, ResponseErrorKind errorKind = ResponseErrorKind.BadRequest, int? httpStatusCode = null)
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
                },
                errorKind: errorKind,
                httpStatusCode: httpStatusCode);
        }

        /// <summary>
        /// HTTP status that, with <see cref="ResponseErrorKind.Unavailable"/>, says the plugin refused a tool
        /// call before running any of it (see <see cref="NotExecuted"/>).
        /// </summary>
        public const int NotExecutedStatusCode = 503;

        /// <summary>
        /// A refusal for a tool call the plugin did NOT start at all (a reload gate, shutting down before
        /// the call began). The server sends such a call again once the plugin is back, within its reconnect
        /// wait. Never use it for a call that may have started: that call would run twice.
        /// </summary>
        public static ResponseCallTool NotExecuted(string message)
            => Error(message, ResponseErrorKind.Unavailable, NotExecutedStatusCode);

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

        public static ResponseCallTool ErrorStructured(JsonNode? structuredContent, ResponseErrorKind errorKind = ResponseErrorKind.Internal, int? httpStatusCode = null)
        {
            return new ResponseCallTool(
                structuredContent: structuredContent,
                status: ResponseStatus.Error,
                errorKind: errorKind,
                httpStatusCode: httpStatusCode);
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
                ContentBlock.CreateImage(data, mimeType)
            };

            if (!string.IsNullOrEmpty(message))
                content.Insert(0, ContentBlock.CreateText(message));

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
                ContentBlock.CreateAudio(data, mimeType)
            };

            if (!string.IsNullOrEmpty(message))
                content.Insert(0, ContentBlock.CreateText(message));

            return new ResponseCallTool(
                status: ResponseStatus.Success,
                content: content);
        }

        /// <summary>
        /// Creates a response with multiple content blocks.
        /// Use this when returning mixed content (e.g., text + images + audio).
        /// </summary>
        /// <param name="status">Response status</param>
        /// <param name="contentBlocks">Array of content blocks to include</param>
        public static ResponseCallTool WithContent(ResponseStatus status, params ContentBlock[] contentBlocks)
        {
            return new ResponseCallTool(
                status: status,
                content: contentBlocks.ToList());
        }
    }
}
