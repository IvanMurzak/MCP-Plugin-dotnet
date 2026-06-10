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
    /// An <see cref="IRunTool"/> whose JSON schemas are supplied externally at runtime and whose body is an
    /// async delegate, rather than a reflected method. This is the building block for hosts that define their
    /// tool set dynamically (e.g. a sidecar that mirrors an external editor's tool manifest): the
    /// <see cref="InputSchema"/>/<see cref="OutputSchema"/> arrive as plain <see cref="JsonNode"/>s and the
    /// <see cref="Run"/> implementation forwards the raw arguments to the supplied <paramref name="handler"/>.
    ///
    /// <para>
    /// Register a proxy tool against a built plugin via
    /// <c>plugin.McpManager.ToolManager.AddTool(name, proxyTool)</c> (which fires the tools-updated /
    /// list-changed notification), and remove it via <c>RemoveTool(name)</c>. Toggle visibility with the
    /// settable <see cref="Enabled"/> property (default <see langword="true"/>).
    /// </para>
    /// </summary>
    public sealed class ProxyTool : IRunTool
    {
        private readonly Func<string, IReadOnlyDictionary<string, JsonElement>?, CancellationToken, Task<ResponseCallTool>> _handler;
        private int? _cachedTokenCount;

        public string Name { get; }
        public string? Title { get; }
        public string? Description { get; }
        public string? SkillDescription { get; }
        public string? SkillBody { get; }
        public JsonNode? InputSchema { get; }
        public JsonNode? OutputSchema { get; }
        public bool? ReadOnlyHint { get; }
        public bool? DestructiveHint { get; }
        public bool? IdempotentHint { get; }
        public bool? OpenWorldHint { get; }

        /// <summary>
        /// Whether this tool is exposed to MCP clients. Settable so a host can toggle a runtime tool on/off;
        /// disabled tools are filtered out of the enabled-tools view by the tool manager. Defaults to <see langword="true"/>.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Semantic token count for this tool, computed once (then cached) from the same chars/4 approximation
        /// used by <see cref="RunTool"/> via the shared <see cref="ToolTokenCount.Calculate"/> helper.
        /// </summary>
        public int TokenCount
        {
            get
            {
                if (_cachedTokenCount.HasValue)
                    return _cachedTokenCount.Value;

                _cachedTokenCount = ToolTokenCount.Calculate(Name, Title, Description, InputSchema, OutputSchema);
                return _cachedTokenCount.Value;
            }
        }

        /// <summary>
        /// Creates a runtime/proxy tool.
        /// </summary>
        /// <param name="name">Unique tool name. Required.</param>
        /// <param name="title">Human-readable title. Optional.</param>
        /// <param name="description">Tool description shown to the AI client. Optional.</param>
        /// <param name="skillDescription">Optional concise SKILL.md description override.</param>
        /// <param name="skillBody">Optional long-form SKILL.md body.</param>
        /// <param name="inputSchema">JSON Schema for the tool inputs, supplied externally. Optional.</param>
        /// <param name="outputSchema">JSON Schema for the tool output, supplied externally. Optional.</param>
        /// <param name="readOnlyHint">MCP read-only behavior hint. Optional.</param>
        /// <param name="destructiveHint">MCP destructive behavior hint. Optional.</param>
        /// <param name="idempotentHint">MCP idempotency behavior hint. Optional.</param>
        /// <param name="openWorldHint">MCP open-world behavior hint. Optional.</param>
        /// <param name="handler">Async delegate invoked on each call with the request id, raw named arguments, and a cancellation token. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> or <paramref name="handler"/> is <see langword="null"/>.</exception>
        public ProxyTool(
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
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Title = title;
            Description = description;
            SkillDescription = skillDescription;
            SkillBody = skillBody;
            InputSchema = inputSchema;
            OutputSchema = outputSchema;
            ReadOnlyHint = readOnlyHint;
            DestructiveHint = destructiveHint;
            IdempotentHint = idempotentHint;
            OpenWorldHint = openWorldHint;
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <summary>
        /// Dispatches the call to the supplied async handler. The raw <paramref name="namedParameters"/> are
        /// forwarded unmodified — the handler owns argument interpretation against the external schema.
        /// </summary>
        public Task<ResponseCallTool> Run(string requestId, IReadOnlyDictionary<string, JsonElement>? namedParameters, CancellationToken cancellationToken = default)
            => _handler(requestId, namedParameters, cancellationToken);
    }
}
