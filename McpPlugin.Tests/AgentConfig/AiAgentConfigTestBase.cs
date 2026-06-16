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
using System.IO;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    /// <summary>
    /// Shared fixture for the ported AI-agent config tests: a per-test temp config file
    /// (deleted on dispose) plus the stand-in values for the engine-supplied settings
    /// (executable path, port, host, timeout) the original Unity tests read from
    /// <c>McpServerManager</c> / <c>UnityMcpPluginEditor</c>.
    /// </summary>
    public abstract class AiAgentConfigTestBase : IDisposable
    {
        protected readonly string TempConfigPath;

        // Stand-ins for the Unity statics the original tests referenced.
        protected const string ExecutableName = "ai-game-developer-mcp-server";
        protected static readonly string ExecutableFullPath = OperatingSystem.IsWindows()
            ? $"C:/Tools/{ExecutableName}.exe"
            : $"/usr/local/bin/{ExecutableName}";
        protected const int Port = 50000;
        protected const int TimeoutMs = 30000;
        protected const string Host = "http://localhost:50000/mcp";

        protected AiAgentConfigTestBase()
        {
            TempConfigPath = Path.GetTempFileName();
        }

        public void Dispose()
        {
            if (File.Exists(TempConfigPath))
                File.Delete(TempConfigPath);
            GC.SuppressFinalize(this);
        }
    }
}
