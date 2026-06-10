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
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// Factory for constructing runtime tools whose schemas are supplied externally and whose body is an
    /// async delegate. Registered as a DI singleton via
    /// <see cref="McpPluginBuilder.WithDynamicToolFactory"/>, so a host can resolve it and create
    /// <see cref="IRunTool"/> instances to register against the tool manager at runtime.
    /// </summary>
    public interface IDynamicToolFactory
    {
        /// <summary>
        /// Creates a runtime/proxy <see cref="IRunTool"/> from externally supplied schemas and an async handler.
        /// </summary>
        IRunTool CreateProxyTool(
            string name,
            string? title,
            string? description,
            string? skillDescription,
            string? skillBody,
            JsonNode? inputSchema,
            JsonNode? outputSchema,
            bool? readOnlyHint,
            bool? destructiveHint,
            bool? idempotentHint,
            bool? openWorldHint,
            Func<string, IReadOnlyDictionary<string, JsonElement>?, CancellationToken, Task<ResponseCallTool>> handler);
    }

    /// <summary>
    /// Default <see cref="IDynamicToolFactory"/> implementation that produces <see cref="ProxyTool"/> instances.
    /// </summary>
    public sealed class ProxyToolFactory : IDynamicToolFactory
    {
        public IRunTool CreateProxyTool(
            string name,
            string? title,
            string? description,
            string? skillDescription,
            string? skillBody,
            JsonNode? inputSchema,
            JsonNode? outputSchema,
            bool? readOnlyHint,
            bool? destructiveHint,
            bool? idempotentHint,
            bool? openWorldHint,
            Func<string, IReadOnlyDictionary<string, JsonElement>?, CancellationToken, Task<ResponseCallTool>> handler)
            => new ProxyTool(
                name: name,
                title: title,
                description: description,
                skillDescription: skillDescription,
                skillBody: skillBody,
                inputSchema: inputSchema,
                outputSchema: outputSchema,
                readOnlyHint: readOnlyHint,
                destructiveHint: destructiveHint,
                idempotentHint: idempotentHint,
                openWorldHint: openWorldHint,
                handler: handler);
    }
}
