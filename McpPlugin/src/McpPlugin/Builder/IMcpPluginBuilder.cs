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
        IMcpPluginBuilder WithTool(Type classType, MethodInfo methodInfo);
        IMcpPluginBuilder WithTool(McpPluginToolAttribute attribute, Type classType, MethodInfo methodInfo);
        IMcpPluginBuilder WithTool(string name, string? title, Type classType, MethodInfo methodInfo);
        IMcpPluginBuilder AddTool(string name, IRunTool runner);
        IMcpPluginBuilder WithTools<T>();
        IMcpPluginBuilder WithTools(params Type[] targetTypes);
        IMcpPluginBuilder WithTools(IEnumerable<Type> targetTypes);
        IMcpPluginBuilder WithTools(Type classType);
        IMcpPluginBuilder WithToolsFromAssembly(IEnumerable<Assembly> assemblies);
        IMcpPluginBuilder WithToolsFromAssembly(Assembly? assembly = null);

        // Prompt methods
        IMcpPluginBuilder WithPrompt(string name, Type classType, MethodInfo methodInfo);
        IMcpPluginBuilder AddPrompt(string name, IRunPrompt runner);
        IMcpPluginBuilder WithPrompts<T>();
        IMcpPluginBuilder WithPrompts(params Type[] targetTypes);
        IMcpPluginBuilder WithPrompts(IEnumerable<Type> targetTypes);
        IMcpPluginBuilder WithPrompts(Type classType);
        IMcpPluginBuilder WithPromptsFromAssembly(IEnumerable<Assembly> assemblies);
        IMcpPluginBuilder WithPromptsFromAssembly(Assembly? assembly = null);

        // Resource methods
        IMcpPluginBuilder WithResource(Type classType, MethodInfo getContentMethod);
        IMcpPluginBuilder AddResource(IRunResource resourceParams);
        IMcpPluginBuilder WithResources<T>();
        IMcpPluginBuilder WithResources(params Type[] targetTypes);
        IMcpPluginBuilder WithResources(IEnumerable<Type> targetTypes);
        IMcpPluginBuilder WithResources(Type classType);
        IMcpPluginBuilder WithResourcesFromAssembly(IEnumerable<Assembly> assemblies);
        IMcpPluginBuilder WithResourcesFromAssembly(Assembly? assembly = null);

        // Configuration methods
        IMcpPluginBuilder AddLogging(Action<ILoggingBuilder> loggingBuilder);
        IMcpPluginBuilder WithConfig(Action<ConnectionConfig> config);
        IMcpPluginBuilder WithConfigFromArgsOrEnv(string[]? args = null);
        IMcpPlugin Build(Reflector reflector);

        // Ignore Assembly methods
        IMcpPluginBuilder IgnoreAssembly(Assembly assembly);
        IMcpPluginBuilder IgnoreAssembly(string assemblyName);
        IMcpPluginBuilder IgnoreAssemblies(IEnumerable<Assembly> assemblies);
        IMcpPluginBuilder IgnoreAssemblies(params string[] assemblyNames);

        // Ignore Namespace methods
        IMcpPluginBuilder IgnoreNamespace(string namespaceName);
        IMcpPluginBuilder IgnoreNamespaces(params string[] namespaceNames);

        // Remove Ignored Assembly methods
        IMcpPluginBuilder RemoveIgnoredAssembly(Assembly assembly);
        IMcpPluginBuilder RemoveIgnoredAssembly(string assemblyName);
        IMcpPluginBuilder RemoveIgnoredAssemblies(IEnumerable<Assembly> assemblies);
        IMcpPluginBuilder RemoveIgnoredAssemblies(params string[] assemblyNames);

        // Remove Ignored Namespace methods
        IMcpPluginBuilder RemoveIgnoredNamespace(string namespaceName);
        IMcpPluginBuilder RemoveIgnoredNamespaces(params string[] namespaceNames);

        // Ignore counters
        int GetIgnoredAssembliesCount();
        int GetIgnoredTypesCount();

        // Clear Ignored methods
        IMcpPluginBuilder ClearIgnoredAssemblies();
        IMcpPluginBuilder ClearIgnoredNamespaces();
        IMcpPluginBuilder ClearAllIgnored();
    }
}
