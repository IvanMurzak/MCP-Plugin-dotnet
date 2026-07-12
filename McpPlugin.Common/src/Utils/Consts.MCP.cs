/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
using System.Text.Json;
using System.Text.Json.Nodes;

namespace com.IvanMurzak.McpPlugin.Common
{
    public static partial class Consts
    {
        public static partial class MCP
        {
            public static readonly JsonElement EmptyInputSchema = JsonDocument.Parse("{ \"type\": \"object\", \"additionalProperties\": false }").RootElement;

            private static readonly JsonNode _emptyInputSchemaNodeTemplate = new JsonObject()
            {
                ["type"] = "object",
                ["additionalProperties"] = false
            };

            public static JsonNode EmptyInputSchemaNode => _emptyInputSchemaNodeTemplate.DeepClone();

            public static class Plugin
            {
                public const int LinesLimit = 1000;

                public static partial class Args
                {
                    public const string McpServerEndpoint = "mcp-server-endpoint";
                    public const string McpServerTimeout = "mcp-server-timeout";
                    // The plugin-side shared-token args/env (`mcp-plugin-token` / `MCP_PLUGIN_TOKEN`) were
                    // removed in mcp-authorize b7: the plugin now presents an auto-refreshed account JWT via
                    // ConnectionConfig.CredentialProvider (machine-store), never a static shared token.
                    public const string McpSkillsFolder = "mcp-skills-folder";
                }

                public static class Env
                {
                    public const string McpServerEndpoint = "MCP_SERVER_ENDPOINT";
                    public const string McpServerTimeout = "MCP_SERVER_TIMEOUT";
                    public const string McpSkillsFolder = "MCP_SKILLS_FOLDER";
                }
            }
            public static partial class Server
            {
                public static partial class Args
                {
                    public const string Port = "port";
                    public const string PluginTimeout = "plugin-timeout";
                    public const string ClientTransportMethod = "client-transport";
                    public const string Token = "token";
                    public const string Authorization = "authorization";
                    public const string IdleTimeoutSeconds = "idle-timeout-seconds";
                    public const string MaxIdleSessionCount = "max-idle-session-count";

                    // OAuth resource-server arguments (mcp-authorize b2).
                    // <c>auth</c> is the target-state name for <c>authorization</c> ({none|oauth});
                    // the legacy <c>authorization</c> arg is retained until the legacy surface is
                    // removed (mcp-authorize b5).
                    public const string Auth = "auth";
                    public const string AuthIssuer = "auth-issuer";
                    public const string PublicUrl = "public-url";
                    public const string Bind = "bind";

                    // Project pin (mcp-authorize b3, design 04 D14). The stdio spawn arg carrying the
                    // session's project pin (first 8 hex chars of the ProjectIdentity SHA-256). The
                    // HTTP equivalent is the /p/&lt;pin&gt; URL path segment.
                    public const string Project = "project";
                }

                /// <summary>
                /// Query-string parameters an engine plugin sends on its SignalR hub connection to
                /// register its instance metadata in <c>oauth</c> account routing (mcp-authorize b3,
                /// design 04). Non-secret; the credential itself always travels in the
                /// <c>Authorization</c> header (never the query). The wire format is finalized in b7.
                /// </summary>
                public static class HubQuery
                {
                    public const string InstanceId = "instance_id";
                    public const string Engine = "engine";
                    public const string ProjectName = "project_name";
                    public const string ProjectPathHash = "project_path_hash";
                    public const string MachineName = "machine_name";
                }

                public static partial class Env
                {
                    public const string Port = "MCP_PLUGIN_PORT";
                    public const string PluginTimeout = "MCP_PLUGIN_CLIENT_TIMEOUT";
                    public const string ClientTransportMethod = "MCP_PLUGIN_CLIENT_TRANSPORT";
                    public const string Token = "MCP_PLUGIN_TOKEN";
                    public const string Authorization = "MCP_AUTHORIZATION";
                    public const string IdleTimeoutSeconds = "MCP_PLUGIN_IDLE_TIMEOUT_SECONDS";
                    public const string MaxIdleSessionCount = "MCP_PLUGIN_MAX_IDLE_SESSION_COUNT";

                    // OAuth resource-server environment variables (mcp-authorize b2).
                    public const string Auth = "MCP_AUTH";
                    public const string AuthIssuer = "MCP_AUTH_ISSUER";
                    public const string PublicUrl = "MCP_PUBLIC_URL";
                    public const string Bind = "MCP_BIND";
                }

                /// <summary>
                /// Default idle-timeout (seconds) for the streamableHttp transport's
                /// <c>HttpServerTransportOptions.IdleTimeout</c>. An idle MCP session is evicted
                /// from the server's in-memory session tracker after this much time without
                /// activity; the next request on an evicted session fails with
                /// <c>HTTP 404</c> / JSON-RPC <c>-32001 "Session not found"</c> and the client
                /// re-initializes (the 404 → reinit reconnect contract).
                /// <para>
                /// The idle timeout only reaps sessions that have <b>no</b> in-flight request and
                /// <b>no</b> open server→client SSE stream: the SDK protects any session whose
                /// <c>IsActive</c> flag is set (a long-running tool call such as a 10-minute
                /// <c>script-execute</c>, or a connected client holding its SSE GET open) from
                /// eviction regardless of this value. A short window is therefore safe for
                /// otherwise-live sessions and reaps only genuinely-disconnected (zombie)
                /// sessions, each of which would otherwise pin its grown <c>SseEventWriter</c>
                /// buffer (up to tens of MiB after a large screenshot / response) until disposed.
                /// We default to 10 minutes (600 seconds) to keep the in-memory footprint bounded;
                /// <see cref="DefaultMaxIdleSessionCount"/> provides the complementary hard ceiling.
                /// Override via the <see cref="Args.IdleTimeoutSeconds"/> CLI argument or
                /// <see cref="Env.IdleTimeoutSeconds"/> environment variable.
                /// </para>
                /// </summary>
                public const int DefaultIdleTimeoutSeconds = 600;

                /// <summary>
                /// Default hard ceiling for the streamableHttp transport's
                /// <c>HttpServerTransportOptions.MaxIdleSessionCount</c>. When the number of idle
                /// (non-<c>IsActive</c>) MCP sessions exceeds this value, the SDK prunes the
                /// least-recently-active idle sessions first — disposing them and returning their
                /// per-session <c>SseEventWriter</c> buffers to the array pool — even before
                /// <see cref="DefaultIdleTimeoutSeconds"/> elapses. This bounds worst-case
                /// retained per-session buffer memory under connection churn. The SDK's own
                /// default is 10,000; we lower it to give generous headroom over real concurrency
                /// while capping the footprint an order of magnitude below the SDK default.
                /// Active sessions (in-flight request or open SSE stream) are never pruned by this
                /// ceiling, so it cannot interrupt a long-running call. Override via the
                /// <see cref="Args.MaxIdleSessionCount"/> CLI argument or
                /// <see cref="Env.MaxIdleSessionCount"/> environment variable.
                /// </summary>
                public const int DefaultMaxIdleSessionCount = 1000;

                public const string DefaultBodyPath = "mcpServers";
                public const string DefaultServerName = "McpPlugin";
                public const string BodyPathDelimiter = "->";

                public static string[] BodyPathSegments(string bodyPath)
                {
                    return bodyPath.Split(BodyPathDelimiter);
                }

                public static JsonNode Config(
                    string executablePath,
                    string serverName = DefaultServerName,
                    string bodyPath = DefaultBodyPath,
                    int port = Hub.DefaultPort,
                    int timeoutMs = Hub.DefaultTimeoutMs)
                {
                    var pathSegments = BodyPathSegments(bodyPath);
                    var root = new JsonObject();
                    var current = root;

                    // Create nested structure following the path segments
                    foreach (var segment in pathSegments)
                    {
                        var child = new JsonObject();
                        current[segment] = child;
                        current = child;
                    }

                    // Place the server configuration at the final location
                    current[serverName] = new JsonObject
                    {
                        ["type"] = "stdio",
                        ["command"] = executablePath,
                        ["args"] = new JsonArray
                        {
                            $"{Args.Port}={port}",
                            $"{Args.PluginTimeout}={timeoutMs}",
                            $"{Args.ClientTransportMethod}={TransportMethod.stdio}"
                        }
                    };

                    return root;
                }

                public enum TransportMethod
                {
                    unknown,
                    stdio,
                    streamableHttp
                }

                public enum AuthOption
                {
                    unknown,
                    none,
                    required,

                    /// <summary>
                    /// OAuth 2.1 resource-server mode (mcp-authorize b2). The server validates
                    /// ES256 JWTs against the authorization server's JWKS and opaque tokens via
                    /// introspection; it never mints tokens. Selected via <c>--auth oauth</c> /
                    /// <c>MCP_AUTH=oauth</c>.
                    /// </summary>
                    oauth
                }

                /// <summary>
                /// HTTP request headers recognised by the MCP plugin server beyond
                /// the stock <c>Authorization</c> header.
                /// </summary>
                public static class Headers
                {
                    /// <summary>
                    /// Marks the caller as a trusted in-process client (e.g. our own
                    /// CLI / desktop app). When the request carries this header set
                    /// to <c>"1"</c>, the MCP list endpoints (<c>tools/list</c>,
                    /// <c>prompts/list</c>, <c>resources/list</c>,
                    /// <c>resources/templates/list</c>) return the FULL catalog —
                    /// including primitives whose <c>Enabled</c> flag is <c>false</c>
                    /// — and disabled entries are tagged with
                    /// <c>_meta.enabled = false</c> so the trusted client can tell
                    /// them apart. Any client that does NOT send this header keeps
                    /// the pre-existing behaviour: disabled entries are filtered
                    /// out, and no <c>_meta</c> is emitted by this server.
                    ///
                    /// This is a UX gate, NOT a security boundary — the header is
                    /// trivially spoofable. Pair it with bearer-token auth when
                    /// "disabled-tool exposure" is sensitive.
                    /// </summary>
                    public const string TrustedInternalClient = "X-McpPlugin-Internal-Client";

                    /// <summary>Value the trusted-client header must carry to opt in.</summary>
                    public const string TrustedInternalClientOptInValue = "1";
                }
            }
        }
    }
}
