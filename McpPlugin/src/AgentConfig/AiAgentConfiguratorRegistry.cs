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
using System.Collections.Generic;
using System.Linq;
using com.IvanMurzak.McpPlugin.AgentConfig.Impl;

namespace com.IvanMurzak.McpPlugin.AgentConfig
{
    /// <summary>
    /// Registry of all built-in AI-agent configurators. Lookup by agent id or name; the
    /// list is sorted by display name with the Custom configurator always pinned last
    /// (mirroring the Unity registry's ordering contract).
    /// </summary>
    public static class AiAgentConfiguratorRegistry
    {
        private static readonly IReadOnlyList<AiAgentConfigurator> _configurators = new AiAgentConfigurator[]
            {
                new ClaudeCodeConfigurator(),
                new ClaudeDesktopConfigurator(),
                new VisualStudioCodeCopilotConfigurator(),
                new VisualStudioCopilotConfigurator(),
                new RiderConfigurator(),
                new CursorConfigurator(),
                new GitHubCopilotCliConfigurator(),
                new GeminiConfigurator(),
                new AntigravityConfigurator(),
                new ClineConfigurator(),
                new OpenCodeConfigurator(),
                new CodexConfigurator(),
                new KiloCodeConfigurator(),
                new UnityAiConfigurator(),
                new ZooCodeConfigurator(),
            }
            .OrderBy(c => c.AgentName)
            .Append(new CustomConfigurator()) // Ensure CustomConfigurator is always last
            .ToList();

        /// <summary>All registered configurators (sorted by name, Custom last).</summary>
        public static IReadOnlyList<AiAgentConfigurator> All => _configurators;

        /// <summary>All agent display names, in registry order.</summary>
        public static List<string> GetAgentNames() => _configurators.Select(c => c.AgentName).ToList();

        /// <summary>All agent ids, in registry order.</summary>
        public static List<string> GetAgentIds() => _configurators.Select(c => c.AgentId).ToList();

        /// <summary>Gets a configurator by its agent id, or null when not found.</summary>
        public static AiAgentConfigurator? GetByAgentId(string? agentId)
        {
            if (string.IsNullOrEmpty(agentId))
                return null;

            return _configurators.FirstOrDefault(c => c.AgentId == agentId);
        }

        /// <summary>Gets a configurator by its display name, or null when not found.</summary>
        public static AiAgentConfigurator? GetByAgentName(string? agentName)
        {
            if (string.IsNullOrEmpty(agentName))
                return null;

            return _configurators.FirstOrDefault(c => c.AgentName == agentName);
        }

        /// <summary>Gets the registry index of a configurator by agent id, or -1 when not found.</summary>
        public static int GetIndexByAgentId(string? agentId)
        {
            if (string.IsNullOrEmpty(agentId))
                return -1;

            for (int i = 0; i < _configurators.Count; i++)
            {
                if (_configurators[i].AgentId == agentId)
                    return i;
            }
            return -1;
        }

        /// <summary>Gets the registry index of a configurator by display name, or -1 when not found.</summary>
        public static int GetIndexByAgentName(string? agentName)
        {
            if (string.IsNullOrEmpty(agentName))
                return -1;

            for (int i = 0; i < _configurators.Count; i++)
            {
                if (_configurators[i].AgentName == agentName)
                    return i;
            }
            return -1;
        }
    }
}
