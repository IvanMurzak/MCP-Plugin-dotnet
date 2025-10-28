/*
┌──────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)             │
│  Repository: GitHub (https://github.com/IvanMurzak/Unity-MCP)    │
│  Copyright (c) 2025 Ivan Murzak                                  │
│  Licensed under the Apache License, Version 2.0.                 │
│  See the LICENSE file in the project root for more information.  │
└──────────────────────────────────────────────────────────────────┘
*/

using System;
using com.IvanMurzak.McpPlugin.Common.Model;
using ModelContextProtocol.Protocol;

namespace com.IvanMurzak.McpPlugin.Server
{
    public static class ExtensionsListResourceTemplates
    {
        public static ListResourceTemplatesResult SetError(this ListResourceTemplatesResult target, string message)
        {
            throw new Exception(message);
        }

        public static ResourceTemplate ToResourceTemplate(this ResponseResourceTemplate response)
        {
            return new ResourceTemplate()
            {
                UriTemplate = response.uriTemplate,
                Name = response.name,
                Description = response.description,
                MimeType = response.mimeType
            };
        }
    }
}
