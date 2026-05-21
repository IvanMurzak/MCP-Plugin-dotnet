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
                    public const string McpPluginToken = "mcp-plugin-token";
                    public const string McpSkillsFolder = "mcp-skills-folder";
                }

                public static class Env
                {
                    public const string McpServerEndpoint = "MCP_SERVER_ENDPOINT";
                    public const string McpServerTimeout = "MCP_SERVER_TIMEOUT";
                    public const string McpPluginToken = "MCP_PLUGIN_TOKEN";
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
                }

                public static partial class Env
                {
                    public const string Port = "MCP_PLUGIN_PORT";
                    public const string PluginTimeout = "MCP_PLUGIN_CLIENT_TIMEOUT";
                    public const string ClientTransportMethod = "MCP_PLUGIN_CLIENT_TRANSPORT";
                    public const string Token = "MCP_PLUGIN_TOKEN";
                    public const string Authorization = "MCP_AUTHORIZATION";
                    public const string IdleTimeoutSeconds = "MCP_PLUGIN_IDLE_TIMEOUT_SECONDS";
                }

                /// <summary>
                /// Default idle-timeout (seconds) for the streamableHttp transport's
                /// <c>HttpServerTransportOptions.IdleTimeout</c>. An idle MCP session is evicted
                /// from the server's in-memory session tracker after this much time without
                /// activity. The SDK's own default is 2 hours; we use 10 minutes as a middle
                /// ground that survives typical client reconnect latencies while keeping the
                /// tracker footprint bounded by <c>MaxIdleSessionCount</c>. Override via the
                /// <see cref="Args.IdleTimeoutSeconds"/> CLI argument or
                /// <see cref="Env.IdleTimeoutSeconds"/> environment variable.
                /// </summary>
                public const int DefaultIdleTimeoutSeconds = 600;

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
                    required
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
