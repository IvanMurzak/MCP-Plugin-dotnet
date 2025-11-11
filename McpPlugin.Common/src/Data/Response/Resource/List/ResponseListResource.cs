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
    public class ResponseListResource
    {
        public string Uri { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true; // custom property
        public string? MimeType { get; set; }
        public string? Description { get; set; }
        public long? Size { get; set; }

        public ResponseListResource() { }
        public ResponseListResource(string uri, string name, bool enabled = true, string? mimeType = null, string? description = null, long? size = null)
        {
            this.Uri = uri;
            this.Name = name;
            this.Enabled = enabled;
            this.MimeType = mimeType;
            this.Description = description;
            this.Size = size;
        }
    }
}
