/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Collections.Generic;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace com.IvanMurzak.McpPlugin.Server.Transport
{
    /// <summary>
    /// Steers MCP SDK 2.x clients onto an <c>initialize</c>-handshake protocol revision (2025-11-25 and
    /// earlier) on every transport.
    ///
    /// <para>MCP SDK 2.x also speaks <c>2026-07-28</c>, where a client bootstraps with <c>server/discover</c>
    /// and list_changed notifications are delivered only over <c>subscriptions/listen</c>, tagged with the
    /// subscription id. This server pushes list_changed itself (plugin tools appear after the AI client
    /// connects) and cannot route those through the SDK's subscription fan-out, so a <c>2026-07-28</c> client
    /// would get untagged notifications it never subscribed to (a spec MUST NOT) and could miss tool updates.
    /// Streamable HTTP already refuses <c>2026-07-28</c> before this filter (the server is stateful); this gives
    /// stdio the same answer. The refusal is the SDK's own <c>-32022 UnsupportedProtocolVersion</c> with the
    /// supported list, which makes a 2.x client fall back to <c>initialize</c> on the same connection.</para>
    ///
    /// <para>Only <c>server/discover</c> is refused: it is the probe a dual-era client sends first, and the SDK
    /// commits nothing for it. Any other request has already been through the SDK's metadata filter, which
    /// commits a declared <c>2026-07-28</c> to the session; refusing it then would leave the connection unable
    /// to fall back (the SDK rejects a later <c>initialize</c>), so such a client — one that skips discovery —
    /// is served as the SDK negotiated rather than stranded.</para>
    /// </summary>
    public static class InitializeHandshakeOnlyFilter
    {
        /// <summary>The initialize-handshake revisions the SDK supports, newest first.</summary>
        public static readonly IReadOnlyList<string> SupportedProtocolVersions =
            new[] { "2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05" };

        public static McpMessageHandler Apply(McpMessageHandler next) => (context, cancellationToken) =>
        {
            if (context.JsonRpcMessage is JsonRpcRequest request && request.Method == RequestMethods.ServerDiscover)
            {
                var requested = request.Context?.ProtocolVersion ?? ReadMetaProtocolVersion(request.Params) ?? "2026-07-28";
                throw new UnsupportedProtocolVersionException(
                    requested,
                    SupportedProtocolVersions,
                    $"Protocol version '{requested}' is not supported by this server; use the initialize handshake.");
            }
            return next(context, cancellationToken);
        };

        static string? ReadMetaProtocolVersion(JsonNode? parameters)
            => parameters is JsonObject obj
                && obj["_meta"] is JsonObject meta
                && meta[MetaKeys.ProtocolVersion] is JsonValue value
                && value.TryGetValue(out string? version)
                    ? version
                    : null;
    }
}
