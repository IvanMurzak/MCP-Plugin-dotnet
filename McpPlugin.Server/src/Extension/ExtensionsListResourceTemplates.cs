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
using com.IvanMurzak.McpPlugin.Common.Model;
using ModelContextProtocol.Protocol;

namespace com.IvanMurzak.McpPlugin.Server
{
    public static class ExtensionsListResourceTemplates
    {
        /// <summary>
        /// Degrades a failed <c>resources/templates/list</c> to an EMPTY catalog, mirroring the tool
        /// path (<see cref="ExtensionsTool.SetError(ListToolsResult, string)"/>). This used to
        /// <c>throw</c>, which turned every routine failure branch of
        /// <see cref="ResourceRouter.ListTemplates"/> — most commonly "the plugin has not connected
        /// yet" — into a hard JSON-RPC <c>-32603</c> protocol error for the client. The failure
        /// message is not dropped: the router logs it as a warning before degrading.
        /// </summary>
        public static ListResourceTemplatesResult SetError(this ListResourceTemplatesResult target, string message)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            target.ResourceTemplates = new List<ResourceTemplate>();

            return target;
        }

        public static ResourceTemplate ToResourceTemplate(this ResponseResourceTemplate response)
        {
            return new ResourceTemplate()
            {
                UriTemplate = response.UriTemplate,
                Name = response.Name,
                Description = response.Description,
                MimeType = response.MimeType,
                // See ExtensionsListMeta — disabled primitives surface `_meta.enabled = false`
                // for trusted clients; null for enabled keeps the default wire shape unchanged.
                Meta = ExtensionsListMeta.BuildEnabledMeta(response.Enabled)
            };
        }
    }
}
