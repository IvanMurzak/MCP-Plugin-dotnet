/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
namespace com.IvanMurzak.McpPlugin.Common
{
    public static partial class Consts
    {
        public static class MimeType
        {
            // Text types
            public const string TextPlain = "text/plain";
            public const string TextHtml = "text/html";
            public const string TextJson = "application/json";
            public const string TextXml = "application/xml";
            public const string TextYaml = "application/x-yaml";
            public const string TextCsv = "text/csv";
            public const string TextMarkdown = "text/markdown";
            public const string TextJavascript = "application/javascript";

            // Image types
            public const string ImagePng = "image/png";
            public const string ImageJpeg = "image/jpeg";
            public const string ImageGif = "image/gif";
            public const string ImageWebp = "image/webp";
            public const string ImageSvg = "image/svg+xml";

            // Audio types
            public const string AudioMpeg = "audio/mpeg";
            public const string AudioWav = "audio/wav";
            public const string AudioOgg = "audio/ogg";
            public const string AudioWebm = "audio/webm";
        }
    }
}
