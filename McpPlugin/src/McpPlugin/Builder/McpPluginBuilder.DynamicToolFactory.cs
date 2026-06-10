/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using Microsoft.Extensions.DependencyInjection;

namespace com.IvanMurzak.McpPlugin
{
    public partial class McpPluginBuilder
    {
        /// <summary>
        /// Registers the <see cref="IDynamicToolFactory"/> (backed by <see cref="ProxyToolFactory"/>) as a
        /// DI singleton. Resolve it from the built plugin's service provider to create runtime
        /// <see cref="ProxyTool"/> instances and add them to the tool manager at runtime.
        /// </summary>
        public virtual IMcpPluginBuilder WithDynamicToolFactory()
        {
            ThrowIfBuilt();

            _services.AddSingleton<IDynamicToolFactory, ProxyToolFactory>();
            return this;
        }
    }
}
