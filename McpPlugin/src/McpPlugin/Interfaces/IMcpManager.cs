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
using com.IvanMurzak.ReflectorNet;
using R3;

namespace com.IvanMurzak.McpPlugin
{
    public interface IMcpManager : IDisposable
    {
        Reflector Reflector { get; }
        Observable<Unit> OnForceDisconnect { get; }
        IToolManager? ToolManager { get; }
        IPromptManager? PromptManager { get; }
        IResourceManager? ResourceManager { get; }
    }
}
