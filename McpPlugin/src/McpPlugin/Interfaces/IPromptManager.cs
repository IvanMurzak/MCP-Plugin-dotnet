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
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using R3;

namespace com.IvanMurzak.McpPlugin
{
    public interface IPromptManager : IClientPromptHub, IDisposable
    {
        Observable<Unit> OnPromptsUpdated { get; }
        int EnabledPromptsCount { get; }
        int TotalPromptsCount { get; }
        bool HasPrompt(string name);
        bool AddPrompt(IRunPrompt runner);
        bool RemovePrompt(string name);
        bool IsPromptEnabled(string name);
        bool SetPromptEnabled(string name, bool enabled);
    }
}
