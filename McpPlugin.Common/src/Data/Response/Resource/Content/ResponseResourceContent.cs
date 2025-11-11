/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

namespace com.IvanMurzak.McpPlugin.Common.Model
{
    public class ResponseResourceContent
    {
        public string Uri { get; set; } = string.Empty;
        public string? MimeType { get; set; }
        public string? Text { get; set; }
        public string? Blob { get; set; }

        public ResponseResourceContent() { }
        public ResponseResourceContent(string uri, string? mimeType = null, string? text = null, string? blob = null)
        {
            this.Uri = uri;
            this.MimeType = mimeType;
            this.Text = text;
            this.Blob = blob;
        }

        public static ResponseResourceContent CreateText(string uri, string text, string? mimeType = null)
            => new ResponseResourceContent(uri, mimeType: mimeType, text: text);

        public static ResponseResourceContent CreateBlob(string uri, string blob, string? mimeType = null)
            => new ResponseResourceContent(uri, mimeType: mimeType, blob: blob);
    }
}
