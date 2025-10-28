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
    public static class ExtensionsListResources
    {
        public static ListResourcesResult SetError(this ListResourcesResult target, string message)
        {
            throw new Exception(message);
        }

        public static Resource ToResource(this IResponseListResource response)
        {
            return new Resource()
            {
                Uri = response.uri,
                Name = response.name,
                Description = response.description,
                MimeType = response.mimeType
            };
        }
    }
}
