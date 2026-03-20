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
using System.Linq;
using System.Reflection;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Skills;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

        protected readonly List<SkillMemberData> _skillFields = new();

        // Ignore configuration for filtering assemblies, namespaces, and types
        protected readonly McpPluginBuilderIgnoreConfig _ignoreConfig = new();

        // Optional externally provided config instance (set via SetConfig)
        protected ConnectionConfig? _externalConfig;

        // Lazy assembly scanning - store assemblies to scan later
        protected readonly List<Assembly> _toolAssemblies = new();
        protected readonly List<Assembly> _promptAssemblies = new();
        protected readonly List<Assembly> _resourceAssemblies = new();
        protected readonly List<Assembly> _skillAssemblies = new();

        // Lazy type scanning - store types to scan later
        protected readonly List<Type> _toolTypes = new();
        protected readonly List<Type> _promptTypes = new();
        protected readonly List<Type> _resourceTypes = new();
        protected readonly List<Type> _skillTypes = new();

        protected bool isBuilt = false;
        protected bool _skillFileGeneratorSet = false;

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

            _services.AddSingleton<McpSystemToolManager>();
            _services.AddSingleton<ISystemToolManager>(sp => sp.GetRequiredService<McpSystemToolManager>());

            _services.AddSingleton<IMcpPlugin, McpPlugin>();
            _services.AddSingleton<IMcpManagerHub, McpManagerClientHub>();

            _services.AddSingleton<McpManager>();
            _services.AddSingleton<IMcpManager>(sp => sp.GetRequiredService<McpManager>());
            _services.AddSingleton<IClientMcpManager>(sp => sp.GetRequiredService<McpManager>());

            _services.AddSingleton<ISkillFileGenerator, SkillFileGenerator>();
        }

        #region Tool
        public virtual IMcpPluginBuilder WithTool(Type classType, MethodInfo methodInfo)
        {
            ThrowIfBuilt();

            var attribute = methodInfo.GetCustomAttribute<McpPluginToolAttribute>();
            return WithTool(attribute!, classType, methodInfo);
        }
        public virtual IMcpPluginBuilder WithTool(string name, string? title, Type classType, MethodInfo methodInfo)
        {
            ThrowIfBuilt();

            var attribute = new McpPluginToolAttribute(name, title);
            return WithTool(attribute, classType, methodInfo);
        }
        public virtual IMcpPluginBuilder WithTool(McpPluginToolAttribute attribute, Type classType, MethodInfo methodInfo)
        {
            ThrowIfBuilt();

            if (attribute == null)
            {
                _logger?.LogWarning($"Method {classType.FullName}{methodInfo.Name} does not have a '{nameof(McpPluginToolAttribute)}'.");
                return this;
            }

            if (string.IsNullOrEmpty(attribute.Name))
                throw new ArgumentException($"Tool name cannot be null or empty. Type: {classType.Name}, Method: {methodInfo.Name}");

            _toolMethods.Add(new ToolMethodData
            (
                classType: classType,
                methodInfo: methodInfo,
                attribute: attribute
            ));
            return this;
        }
        public virtual IMcpPluginBuilder AddTool(string name, IRunTool runner)
        {
            ThrowIfBuilt();

            if (_toolRunners.ContainsKey(name))
                throw new ArgumentException($"Tool with name '{name}' already exists.");

            _toolRunners.Add(name, runner);
            return this;
        }
        #endregion

        #region Prompt
        public virtual IMcpPluginBuilder WithPrompt(string name, Type classType, MethodInfo methodInfo)
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
        public virtual IMcpPluginBuilder AddPrompt(string name, IRunPrompt runner)
        {
            ThrowIfBuilt();

            if (_promptRunners.ContainsKey(name))
                throw new ArgumentException($"Prompt with name '{name}' already exists.");

            _promptRunners.Add(name, runner);
            return this;
        }
        #endregion

        #region Resource
        public virtual IMcpPluginBuilder WithResource(Type classType, MethodInfo getContentMethod)
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
        public virtual IMcpPluginBuilder AddResource(IRunResource resourceParams)
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
        public virtual IMcpPluginBuilder AddLogging(Action<ILoggingBuilder> loggingBuilder)
        {
            ThrowIfBuilt();

            _services.AddLogging(loggingBuilder);
            return this;
        }

        public virtual IMcpPluginBuilder SetConfig(ConnectionConfig config)
        {
            ThrowIfBuilt();

            _externalConfig = config ?? throw new ArgumentNullException(nameof(config));
            return this;
        }

        public virtual IMcpPluginBuilder WithConfig(Action<ConnectionConfig> config)
        {
            ThrowIfBuilt();

            if (_externalConfig != null)
                config(_externalConfig);
            else
                _services.Configure(config);
            return this;
        }

        public virtual IMcpPluginBuilder WithSkillFileGenerator<T>()
            where T : class, ISkillFileGenerator
        {
            ThrowIfBuilt();
            ThrowIfSkillFileGeneratorSet();

            _skillFileGeneratorSet = true;
            _services.AddSingleton<ISkillFileGenerator, T>();
            return this;
        }

        public virtual IMcpPluginBuilder WithSkillFileGenerator(ISkillFileGenerator instance)
        {
            ThrowIfBuilt();
            ThrowIfSkillFileGeneratorSet();

            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            _skillFileGeneratorSet = true;
            _services.AddSingleton<ISkillFileGenerator>(instance);
            return this;
        }

        public virtual IMcpPluginBuilder WithConfigFromArgsOrEnv(string[]? args = null) => WithConfig(config =>
        {
            config.Host = ConnectionConfig.GetEndpointFromArgsOrEnv(args);
            config.Token = ConnectionConfig.GetTokenFromArgsOrEnv(args);
            config.TimeoutMs = ConnectionConfig.GetTimeoutFromArgsOrEnv(args);
        });

        /// <summary>
        /// Builds the plugin instance. This is a one-time operation - once Build() is called, the builder cannot be modified or built again.
        /// </summary>
        /// <param name="reflector">The reflector instance used for reflection operations.</param>
        /// <returns>The built plugin instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown if the reflector is null.</exception>
        public virtual IMcpPlugin Build(Reflector reflector)
        {
            ThrowIfBuilt();

            if (reflector == null)
                throw new ArgumentNullException(nameof(reflector));

            // Process all assemblies with caching optimization
            ProcessAllAssemblies();

            _services.AddSingleton(reflector);

            var standardMethods = _toolMethods.Where(m => m.Attribute.ToolType == McpToolType.Standard).ToList();
            var systemMethods = _toolMethods.Where(m => m.Attribute.ToolType == McpToolType.System).ToList();

            var standardRunners = _toolRunners.Where(r => r.Value.ToolType == McpToolType.Standard).ToDictionary(r => r.Key, r => r.Value);
            var systemRunners = _toolRunners.Where(r => r.Value.ToolType == McpToolType.System).ToDictionary(r => r.Key, r => r.Value);

            _services.AddSingleton(new ToolRunnerCollection(reflector, _loggerProvider?.CreateLogger(nameof(ToolRunnerCollection)))
                .Add(standardMethods)
                .Add(standardRunners));

            _services.AddSingleton(new SystemToolRunnerCollection(reflector, _loggerProvider?.CreateLogger(nameof(SystemToolRunnerCollection)))
                .Add(systemMethods)
                .Add(systemRunners));

            _services.AddSingleton(new PromptRunnerCollection(reflector, _loggerProvider?.CreateLogger(nameof(PromptRunnerCollection)))
                .Add(_promptMethods)
                .Add(_promptRunners));

            _services.AddSingleton(new ResourceRunnerCollection(reflector, _loggerProvider?.CreateLogger(nameof(ResourceRunnerCollection)))
                .Add(_resourceMethods)
                .Add(_resourceRunners));

            _services.AddSingleton(new SkillContentCollection(_loggerProvider?.CreateLogger(nameof(SkillContentCollection)))
                .Add(_skillFields));

            if (_externalConfig != null)
                _services.AddSingleton<IOptions<ConnectionConfig>>(new OptionsWrapper<ConnectionConfig>(_externalConfig));

            ServiceProvider = _services.BuildServiceProvider();
            isBuilt = true;

            return ServiceProvider.GetRequiredService<IMcpPlugin>();
        }

        protected virtual void ThrowIfBuilt()
        {
            if (isBuilt)
                throw new InvalidOperationException("The builder has already been built.");
        }

        protected virtual void ThrowIfSkillFileGeneratorSet()
        {
            if (_skillFileGeneratorSet)
                throw new InvalidOperationException($"{nameof(ISkillFileGenerator)} has already been set. Only one {nameof(ISkillFileGenerator)} can be registered.");
        }
        #endregion
    }
}
