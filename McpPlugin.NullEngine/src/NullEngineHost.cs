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
        public static string ProjectRoot { get; private set; } = string.Empty;

        /// <summary>Resolved MCP server base URL (<c>--mcp-server-endpoint</c>).</summary>
        public static string Endpoint { get; private set; } = string.Empty;

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

        /// <summary>
        /// Publishes the whole catalog in ONE call, made once by <c>Program</c> before it prints the
        /// ready line.
        ///
        /// <para>One call rather than several setters is deliberate. The endpoint and project root
        /// are ARGUMENTS here instead of properties read back out of this class, so there is no
        /// order in which a caller can get this wrong: reading them as properties meant an
        /// initialisation sequence spread across three statements, and a caller that published the
        /// catalog before assigning them shipped <c>"endpoint": ""</c> in the config resource -
        /// silently, since every count would still be right. The tool COUNT is likewise derived from
        /// the names array rather than passed alongside it, so the two cannot disagree.</para>
        /// </summary>
        public static void SetCatalog(
            string[] toolNames,
            int prompts,
            int resources,
            string pluginVersion,
            string endpoint,
            string projectRoot)
        {
            var names = toolNames ?? Array.Empty<string>();
            var payload = new JsonObject
            {
                ["endpoint"] = endpoint,
                ["project_root"] = projectRoot,
                ["tools"] = names.Length,
                ["prompts"] = prompts,
                ["resources"] = resources,
                ["plugin_version"] = pluginVersion
            };

            Endpoint = endpoint;
            ProjectRoot = projectRoot;

            lock (_gate)
            {
                _toolNames = names;
                _configJson = payload.ToJsonString();
            }
        }
    }
}
