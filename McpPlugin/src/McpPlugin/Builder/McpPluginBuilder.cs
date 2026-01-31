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
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin
{
    public partial class McpPluginBuilder : IMcpPluginBuilder
    {
        protected readonly ILogger? _logger;
        protected readonly ILoggerProvider? _loggerProvider;
        protected readonly IServiceCollection _services;

        protected readonly List<ToolMethodData> _toolMethods = new();
        protected readonly Dictionary<string, IRunTool> _toolRunners = new();

        protected readonly List<PromptMethodData> _promptMethods = new();
        protected readonly Dictionary<string, IRunPrompt> _promptRunners = new();

        protected readonly List<ResourceMethodData> _resourceMethods = new();
        protected readonly Dictionary<string, IRunResource> _resourceRunners = new();

        // Ignore configuration for filtering assemblies, namespaces, and types
        protected readonly McpPluginBuilderIgnoreConfig _ignoreConfig = new();

        // Lazy assembly scanning - store assemblies to scan later
        protected readonly List<Assembly> _toolAssemblies = new();
        protected readonly List<Assembly> _promptAssemblies = new();
        protected readonly List<Assembly> _resourceAssemblies = new();

        // Lazy type scanning - store types to scan later
        protected readonly List<Type> _toolTypes = new();
        protected readonly List<Type> _promptTypes = new();
        protected readonly List<Type> _resourceTypes = new();

        protected bool isBuilt = false;

        public IServiceCollection Services => _services;
        public ServiceProvider? ServiceProvider { get; private set; }

        public McpPluginBuilder(Version version, ILoggerProvider? loggerProvider = null, IServiceCollection? services = null)
        {
            _loggerProvider = loggerProvider;
            _logger = loggerProvider?.CreateLogger(nameof(McpPluginBuilder));
            _services = services ?? new ServiceCollection();

            if (_loggerProvider != null)
            {
                _services.AddLogging(builder => builder.AddProvider(_loggerProvider));
            }
            else
            {
                _services.AddLogging();
            }
            _services.AddSingleton(version);
            _services.AddSingleton<IConnectionManager, ConnectionManager>();
            _services.AddSingleton<IHubConnectionProvider, HubConnectionProvider>();

            _services.AddSingleton<IToolManager, McpToolManager>();
            _services.AddSingleton<IPromptManager, McpPromptManager>();
            _services.AddSingleton<IResourceManager, McpResourceManager>();

            _services.AddSingleton<IMcpPlugin, McpPlugin>();
            _services.AddSingleton<IRemoteMcpManagerHub, McpManagerClientHub>();

            _services.AddSingleton<McpManager>();
            _services.AddSingleton<IMcpManager>(sp => sp.GetRequiredService<McpManager>());
            _services.AddSingleton<IClientMcpManager>(sp => sp.GetRequiredService<McpManager>());
        }

        #region Tool
        public virtual McpPluginBuilder WithTool(Type classType, MethodInfo method)
        {
            ThrowIfBuilt();

            var attribute = method.GetCustomAttribute<McpPluginToolAttribute>();
            return WithTool(attribute!, classType, method);
        }
        public virtual McpPluginBuilder WithTool(string name, string? title, Type classType, MethodInfo method)
        {
            ThrowIfBuilt();

            var attribute = new McpPluginToolAttribute(name, title);
            return WithTool(attribute, classType, method);
        }
        public virtual McpPluginBuilder WithTool(McpPluginToolAttribute attribute, Type classType, MethodInfo method)
        {
            ThrowIfBuilt();

            if (attribute == null)
            {
                _logger?.LogWarning($"Method {classType.FullName}{method.Name} does not have a '{nameof(McpPluginToolAttribute)}'.");
                return this;
            }

            if (string.IsNullOrEmpty(attribute.Name))
                throw new ArgumentException($"Tool name cannot be null or empty. Type: {classType.Name}, Method: {method.Name}");

            _toolMethods.Add(new ToolMethodData
            (
                classType: classType,
                methodInfo: method,
                attribute: attribute
            ));
            return this;
        }
        public virtual McpPluginBuilder AddTool(string name, IRunTool runner)
        {
            ThrowIfBuilt();

            if (_toolRunners.ContainsKey(name))
                throw new ArgumentException($"Tool with name '{name}' already exists.");

            _toolRunners.Add(name, runner);
            return this;
        }
        #endregion

        #region Prompt
        public virtual McpPluginBuilder WithPrompt(string name, Type classType, MethodInfo methodInfo)
        {
            ThrowIfBuilt();

            var attribute = methodInfo.GetCustomAttribute<McpPluginPromptAttribute>();
            if (attribute == null)
            {
                _logger?.LogWarning($"Method {classType.FullName}{methodInfo.Name} does not have a '{nameof(McpPluginPromptAttribute)}'.");
                return this;
            }

            if (string.IsNullOrEmpty(attribute.Name))
                throw new ArgumentException($"Prompt name cannot be null or empty. Type: {classType.Name}, Method: {methodInfo.Name}");

            _promptMethods.Add(new PromptMethodData
            (
                classType: classType,
                methodInfo: methodInfo,
                attribute: attribute
            ));
            return this;
        }
        public virtual McpPluginBuilder AddPrompt(string name, IRunPrompt runner)
        {
            ThrowIfBuilt();

            if (_promptRunners.ContainsKey(name))
                throw new ArgumentException($"Prompt with name '{name}' already exists.");

            _promptRunners.Add(name, runner);
            return this;
        }
        #endregion

        #region Resource
        public virtual McpPluginBuilder WithResource(Type classType, MethodInfo getContentMethod)
        {
            ThrowIfBuilt();

            var attribute = getContentMethod.GetCustomAttribute<McpPluginResourceAttribute>();
            if (attribute == null)
            {
                _logger?.LogWarning($"Method {classType.FullName}{getContentMethod.Name} does not have a '{nameof(McpPluginResourceAttribute)}'.");
                return this;
            }

            var listResourcesMethodName = attribute.ListResources ?? throw new InvalidOperationException($"Method {getContentMethod.Name} does not have a 'ListResources'.");
            var listResourcesMethod = classType.GetMethod(listResourcesMethodName);
            if (listResourcesMethod == null)
                throw new InvalidOperationException($"Method {classType.FullName}{listResourcesMethodName} not found in type {classType.Name}.");

            if (!getContentMethod.ReturnType.IsArray ||
                !typeof(ResponseResourceContent).IsAssignableFrom(getContentMethod.ReturnType.GetElementType()))
                throw new InvalidOperationException($"Method {classType.FullName}{getContentMethod.Name} must return {nameof(ResponseResourceContent)} array.");

            if (!listResourcesMethod.ReturnType.IsArray ||
                !typeof(ResponseListResource).IsAssignableFrom(listResourcesMethod.ReturnType.GetElementType()))
                throw new InvalidOperationException($"Method {classType.FullName}{listResourcesMethod.Name} must return {nameof(ResponseListResource)} array.");

            _resourceMethods.Add(new ResourceMethodData
            (
                classType: classType,
                attribute: attribute,
                getContentMethod: getContentMethod,
                listResourcesMethod: listResourcesMethod
            ));

            return this;
        }
        public virtual McpPluginBuilder AddResource(IRunResource resourceParams)
        {
            ThrowIfBuilt();

            if (_resourceRunners == null)
                throw new ArgumentNullException(nameof(_resourceRunners));

            if (resourceParams == null)
                throw new ArgumentNullException(nameof(resourceParams));

            if (_resourceRunners.ContainsKey(resourceParams.Route))
                throw new ArgumentException($"Resource with routing '{resourceParams.Route}' already exists.");

            _resourceRunners.Add(resourceParams.Route, resourceParams);
            return this;
        }
        #endregion

        #region Other
        public virtual McpPluginBuilder AddLogging(Action<ILoggingBuilder> loggingBuilder)
        {
            ThrowIfBuilt();

            _services.AddLogging(loggingBuilder);
            return this;
        }

        public virtual McpPluginBuilder WithConfig(Action<ConnectionConfig> config)
        {
            ThrowIfBuilt();

            _services.Configure(config);
            return this;
        }

        public virtual McpPluginBuilder WithConfigFromArgsOrEnv(string[]? args = null) => WithConfig(config =>
        {
            config.Host = ConnectionConfig.GetEndpointFromArgsOrEnv(args);
            config.TimeoutMs = ConnectionConfig.GetTimeoutFromArgsOrEnv(args);
        });

        public virtual IMcpPlugin Build(Reflector reflector)
        {
            ThrowIfBuilt();

            if (reflector == null)
                throw new ArgumentNullException(nameof(reflector));

            // Process all assemblies with caching optimization
            ProcessAllAssemblies();

            // Clear cache to free memory
            ClearAttributeCache();

            _services.AddSingleton(reflector);

            _services.AddSingleton(new ToolRunnerCollection(reflector, _loggerProvider?.CreateLogger(nameof(ToolRunnerCollection)))
                .Add(_toolMethods)
                .Add(_toolRunners));

            _services.AddSingleton(new PromptRunnerCollection(reflector, _loggerProvider?.CreateLogger(nameof(PromptRunnerCollection)))
                .Add(_promptMethods)
                .Add(_promptRunners));

            _services.AddSingleton(new ResourceRunnerCollection(reflector, _loggerProvider?.CreateLogger(nameof(ResourceRunnerCollection)))
                .Add(_resourceMethods)
                .Add(_resourceRunners));

            ServiceProvider = _services.BuildServiceProvider();
            isBuilt = true;

            return ServiceProvider.GetRequiredService<IMcpPlugin>();
        }

        protected virtual void ThrowIfBuilt()
        {
            if (isBuilt)
                throw new InvalidOperationException("The builder has already been built.");
        }
        #endregion
    }
}
