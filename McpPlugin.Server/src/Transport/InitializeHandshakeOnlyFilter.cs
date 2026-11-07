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
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace com.IvanMurzak.McpPlugin.Server.Transport
{
    /// <summary>
    /// Keeps every MCP client on an <c>initialize</c>-handshake protocol revision (2025-11-25 and earlier),
    /// on every transport.
    ///
    /// <para>MCP SDK 2.x also speaks <c>2026-07-28</c>, where a client bootstraps with <c>server/discover</c>
    /// and list_changed notifications are delivered only over <c>subscriptions/listen</c>, tagged with the
    /// subscription id. This server pushes list_changed itself (plugin tools appear after the AI client
    /// connects) and cannot route those through the SDK's subscription fan-out, so a <c>2026-07-28</c> client
    /// would get untagged notifications it never subscribed to (a spec MUST NOT) and could miss tool updates.
    /// Streamable HTTP already refuses <c>2026-07-28</c> because the server is stateful; this filter gives stdio
    /// the same answer. The refusal is the SDK's own <c>-32022 UnsupportedProtocolVersion</c> with the supported
    /// list, which makes a 2.x client fall back to <c>initialize</c> on the same connection.</para>
    /// </summary>
    public static class InitializeHandshakeOnlyFilter
    {
        /// <summary>The initialize-handshake revisions the SDK supports, newest first.</summary>
        public static readonly IReadOnlyList<string> SupportedProtocolVersions =
            new[] { "2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05" };

        public static McpMessageHandler Apply(McpMessageHandler next) => (context, cancellationToken) =>
        {
            if (context.JsonRpcMessage is JsonRpcRequest request && TryGetRefusedVersion(request, out var requested))
            {
                throw new UnsupportedProtocolVersionException(
                    requested,
                    SupportedProtocolVersions,
                    $"Protocol version '{requested}' is not supported by this server; use the initialize handshake.");
            }
            return next(context, cancellationToken);
        };

        /// <summary>
        /// A request is refused when it is <c>server/discover</c> or declares (HTTP header or per-request
        /// <c>_meta</c>) a revision outside <see cref="SupportedProtocolVersions"/>. Legacy requests carry
        /// neither, or a supported header value, and pass through untouched.
        /// </summary>
        public static bool TryGetRefusedVersion(JsonRpcRequest request, out string requested)
        {
            var declared = request.Context?.ProtocolVersion ?? ReadMetaProtocolVersion(request.Params);

            if (request.Method == RequestMethods.ServerDiscover)
            {
                requested = declared ?? "2026-07-28";
                return true;
            }

            requested = declared ?? string.Empty;
            return declared != null && !Contains(declared);
        }

        static bool Contains(string version)
        {
            foreach (var supported in SupportedProtocolVersions)
                if (string.Equals(supported, version, StringComparison.Ordinal))
                    return true;
            return false;
        }

        static string? ReadMetaProtocolVersion(JsonNode? parameters)
            => parameters is JsonObject obj
                && obj["_meta"] is JsonObject meta
                && meta[MetaKeys.ProtocolVersion] is JsonValue value
                && value.TryGetValue(out string? version)
                    ? version
                    : null;
    }
}
