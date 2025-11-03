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
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin
{
    public class McpManager : IMcpManager, IClientMcpManager
    {
        protected readonly ILogger _logger;
        protected readonly Reflector _reflector;

        readonly IToolManager? _tools;
        readonly IPromptManager? _prompts;
        readonly IResourceManager? _resources;

        public Reflector Reflector => _reflector;
        public IToolManager? ToolManager => _tools;
        public IPromptManager? PromptManager => _prompts;
        public IResourceManager? ResourceManager => _resources;

        public IClientToolHub? ToolHub => ToolManager;
        public IClientPromptHub? PromptHub => PromptManager;
        public IClientResourceHub? ResourceHub => ResourceManager;

        public McpManager(
            ILogger<McpManager> logger,
            Reflector reflector,
            IToolManager? tools = null,
            IPromptManager? prompts = null,
            IResourceManager? resources = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("Ctor");

            _reflector = reflector ?? throw new ArgumentNullException(nameof(reflector));

            _tools = tools;
            _prompts = prompts;
            _resources = resources;
        }

        public void Dispose()
        {
            _tools?.Dispose();
            _prompts?.Dispose();
            _resources?.Dispose();
        }

        public void ForceDisconnect()
        {
            throw new NotImplementedException();
        }
    }
}
