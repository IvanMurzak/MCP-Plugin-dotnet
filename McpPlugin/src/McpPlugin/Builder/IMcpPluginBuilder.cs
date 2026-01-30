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
using System.Reflection;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin
{
    public interface IMcpPluginBuilder
    {
        IServiceCollection Services { get; }

        // Tool methods
        McpPluginBuilder WithTool(Type classType, MethodInfo method);
        McpPluginBuilder WithTool(McpPluginToolAttribute attribute, Type classType, MethodInfo method);
        McpPluginBuilder WithTool(string name, string? title, Type classType, MethodInfo method);
        McpPluginBuilder AddTool(string name, IRunTool runner);

        // Prompt methods
        McpPluginBuilder WithPrompt(string name, Type classType, MethodInfo method);
        McpPluginBuilder AddPrompt(string name, IRunPrompt runner);

        // Resource methods
        McpPluginBuilder WithResource(Type classType, MethodInfo getContentMethod);
        McpPluginBuilder AddResource(IRunResource resourceParams);

        // Configuration methods
        McpPluginBuilder AddLogging(Action<ILoggingBuilder> loggingBuilder);
        McpPluginBuilder WithConfig(Action<ConnectionConfig> config);
        IMcpPlugin Build(Reflector reflector);
    }
}
