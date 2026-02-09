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
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using R3;

namespace com.IvanMurzak.McpPlugin
{
    public interface IToolManager : IClientToolHub, IDisposable
    {
        Observable<Unit> OnToolsUpdated { get; }
        int EnabledToolsCount { get; }
        int TotalToolsCount { get; }
        long ToolCallCount => 0;
        IEnumerable<IRunTool> GetAllTools();
        bool HasTool(string name);
        bool AddTool(string name, IRunTool runner);
        bool RemoveTool(string name);
        bool IsToolEnabled(string name);
        bool SetToolEnabled(string name, bool enabled);
    }
}
