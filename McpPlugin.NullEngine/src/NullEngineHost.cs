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
using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.NullEngine
{
    /// <summary>
    /// The small amount of state the attribute-scanned tool / prompt / resource classes need from
    /// the running host. They are STATIC classes (that is what <c>[AiToolType]</c> scanning
    /// registers), so there is no constructor to inject through; <c>Program</c> populates this once,
    /// before it prints the ready line.
    /// </summary>
    public static class NullEngineHost
    {
        static readonly object _gate = new object();
        static string[] _toolNames = Array.Empty<string>();
        static string _configJson = "{}";

        /// <summary>
        /// The plugin's own logger, so a tool never has to call <c>Console.WriteLine</c>.
        /// Null until <c>Program</c> has built the plugin.
        /// </summary>
        public static ILogger? Logger { get; set; }

        /// <summary>Resolved host project root (<c>--project-root</c>, else a fresh temp directory).</summary>
        public static string ProjectRoot { get; set; } = string.Empty;

        /// <summary>Resolved MCP server base URL (<c>--mcp-server-endpoint</c>).</summary>
        public static string Endpoint { get; set; } = string.Empty;

        /// <summary>
        /// The registered tool names, ordered ordinally. Serving these from the plugin's own tool
        /// manager (rather than re-reflecting over the assembly) is what makes <c>list-self</c>
        /// report what the plugin ACTUALLY registered.
        /// </summary>
        public static string[] ToolNames
        {
            get
            {
                lock (_gate)
                    return (string[])_toolNames.Clone();
            }
        }

        /// <summary>The <c>null-engine://config</c> resource body: the resolved host configuration.</summary>
        public static string ConfigJson
        {
            get
            {
                lock (_gate)
                    return _configJson;
            }
        }

        public static void SetToolNames(string[] names)
        {
            lock (_gate)
                _toolNames = names ?? Array.Empty<string>();
        }

        public static void SetCounts(int tools, int prompts, int resources, string pluginVersion)
        {
            var payload = new JsonObject
            {
                ["endpoint"] = Endpoint,
                ["project_root"] = ProjectRoot,
                ["tools"] = tools,
                ["prompts"] = prompts,
                ["resources"] = resources,
                ["plugin_version"] = pluginVersion
            };

            lock (_gate)
                _configJson = payload.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }
    }
}
