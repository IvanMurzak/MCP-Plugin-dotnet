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
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using ModelContextProtocol.Protocol;

namespace com.IvanMurzak.McpPlugin.Server
{
    public static class ExtensionsReadResource
    {
        public static ReadResourceResult SetError(this ReadResourceResult target, string uri, string message)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            var error = new TextResourceContents()
            {
                Uri = uri,
                MimeType = Consts.MimeType.TextPlain,
                Text = message
            };

            target.Contents ??= new List<ResourceContents>(1);
            target.Contents.Clear();
            target.Contents.Add(error);

            return target;
        }

        public static ResourceContents ToResourceContents(this ResponseResourceContent response)
        {
            if (response!.Text != null)
                return new TextResourceContents()
                {
                    Uri = response.Uri,
                    MimeType = response.MimeType,
                    Text = response.Text
                };

            if (response!.Blob != null)
                return new BlobResourceContents()
                {
                    Uri = response.Uri,
                    MimeType = response.MimeType,
                    Blob = Convert.FromBase64String(response.Blob)
                };

            throw new InvalidOperationException("Resource contents is null");
        }
    }
}
