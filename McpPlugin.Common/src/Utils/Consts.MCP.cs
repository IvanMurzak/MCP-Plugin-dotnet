/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
using System.Text.Json.Nodes;

namespace com.IvanMurzak.McpPlugin.Common
{
    public static partial class Consts
    {
        public static partial class MCP
        {
            public static class Plugin
            {
                public const int LinesLimit = 1000;

                public static partial class Args
                {
                    public const string McpServerEndpoint = "--mcp-server-endpoint";
                    public const string McpServerTimeout = "--mcp-server-timeout";
                }

                public static class Env
                {
                    public const string McpServerEndpoint = "MCP_SERVER_ENDPOINT";
                    public const string McpServerTimeout = "MCP_SERVER_TIMEOUT";
                }
            }
            public static class Server
            {
                public static partial class Args
                {
                    public const string Port = "--port";
                    public const string PluginTimeout = "--plugin-timeout";
                    public const string ClientTransportMethod = "--client-transport";
                }

                public static class Env
                {
                    public const string Port = "MCP_PLUGIN_PORT";
                    public const string PluginTimeout = "MCP_PLUGIN_CLIENT_TIMEOUT";
                    public const string ClientTransportMethod = "MCP_PLUGIN_CLIENT_TRANSPORT";
                }

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
            }
        }
    }
}
