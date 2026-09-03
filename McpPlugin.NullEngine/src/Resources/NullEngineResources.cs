/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
#nullable enable
using System.ComponentModel;
using com.IvanMurzak.McpPlugin.Common.Model;

namespace com.IvanMurzak.McpPlugin.NullEngine.Resources
{
    /// <summary>
    /// The null engine's single resource: the host's own resolved configuration, so a consumer can
    /// read back the endpoint / project root / catalog sizes it is talking to without parsing the
    /// ready line.
    /// </summary>
    [AiResourceType]
    public static class NullEngineResources
    {
        public const string ConfigUri = "null-engine://config";
        public const string ConfigName = "null-engine-config";
        public const string ConfigMimeType = "application/json";
        public const string ConfigDescription = "The resolved null-engine host configuration, as JSON.";

        /// <param name="uri">
        /// Supplied by the resource router from the matched route. The route carries no template
        /// parameters, so this is always <see cref="ConfigUri"/>; it is declared because the router
        /// invokes the method with the matched uri in its named-parameter set.
        /// </param>
        [AiResource(Route = ConfigUri, Name = ConfigName, MimeType = ConfigMimeType, ListResources = nameof(ListConfig))]
        [Description(ConfigDescription)]
        public static ResponseResourceContent[] GetConfig(string uri)
            => new[] { ResponseResourceContent.CreateText(uri, NullEngineHost.ConfigJson, ConfigMimeType) };

        public static ResponseListResource[] ListConfig()
            => new[]
            {
                new ResponseListResource(
                    uri: ConfigUri,
                    name: ConfigName,
                    mimeType: ConfigMimeType,
                    description: ConfigDescription)
            };
    }
}
