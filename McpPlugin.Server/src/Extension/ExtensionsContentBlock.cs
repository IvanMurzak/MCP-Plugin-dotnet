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
using ModelContextProtocol.Protocol;

namespace com.IvanMurzak.McpPlugin.Server
{
    public static class ExtensionsContentBlock
    {
        public static ContentBlock ToContent(this Common.Model.ContentBlock response)
        {
            switch (response.Type)
            {
                case "image":
                    if (response.Data == null)
                        throw new InvalidOperationException("Image content block is missing Data.");
                    return ImageContentBlock.FromBytes(
                        Convert.FromBase64String(response.Data),
                        response.MimeType ?? string.Empty);

                case "audio":
                    if (response.Data == null)
                        throw new InvalidOperationException("Audio content block is missing Data.");
                    return AudioContentBlock.FromBytes(
                        Convert.FromBase64String(response.Data),
                        response.MimeType ?? string.Empty);

                case "resource":
                    if (response.Resource == null)
                        throw new InvalidOperationException("Resource content block is missing Resource.");
                    return new EmbeddedResourceBlock
                    {
                        Resource = response.Resource.ToResourceContents()
                    };

                default:
                    return new TextContentBlock()
                    {
                        Text = response.Text ?? string.Empty
                    };
            }
        }
    }
}
