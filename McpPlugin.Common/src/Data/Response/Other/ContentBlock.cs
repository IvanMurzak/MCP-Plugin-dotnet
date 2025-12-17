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

namespace com.IvanMurzak.McpPlugin.Common.Model
{
    public class ContentBlock
    {
        /// <summary>
        /// The type of content. This determines the structure of the content object. Can be "image", "audio", "text", "resource".
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// The text content of the message.
        /// </summary>
        public string? Text { get; set; }

        /// <summary>
        /// The base64-encoded image or audio data.
        /// </summary>
        public string? Data { get; set; }

        /// <summary>
        /// The MIME type of the content.
        /// </summary>
        public string? MimeType { get; set; }

        /// <summary>
        /// The resource content of the message (if embedded).
        /// </summary>
        public ResponseResourceContent? Resource { get; set; }

        public ContentBlock() { }

        /// <summary>
        /// Creates a text content block.
        /// </summary>
        public static ContentBlock CreateText(string text, string mimeType = Consts.MimeType.TextPlain)
            => new ContentBlock { Type = "text", Text = text, MimeType = mimeType };

        /// <summary>
        /// Creates an image content block from raw bytes.
        /// </summary>
        public static ContentBlock CreateImage(byte[] data, string mimeType)
            => new ContentBlock { Type = "image", Data = Convert.ToBase64String(data), MimeType = mimeType };

        /// <summary>
        /// Creates an image content block from base64-encoded data.
        /// </summary>
        public static ContentBlock CreateImageBase64(string base64Data, string mimeType)
            => new ContentBlock { Type = "image", Data = base64Data, MimeType = mimeType };

        /// <summary>
        /// Creates an audio content block from raw bytes.
        /// </summary>
        public static ContentBlock CreateAudio(byte[] data, string mimeType)
            => new ContentBlock { Type = "audio", Data = Convert.ToBase64String(data), MimeType = mimeType };

        /// <summary>
        /// Creates an audio content block from base64-encoded data.
        /// </summary>
        public static ContentBlock CreateAudioBase64(string base64Data, string mimeType)
            => new ContentBlock { Type = "audio", Data = base64Data, MimeType = mimeType };

        /// <summary>
        /// Creates a resource content block.
        /// </summary>
        public static ContentBlock CreateResource(ResponseResourceContent resource)
            => new ContentBlock { Type = "resource", Resource = resource };
    }
}
