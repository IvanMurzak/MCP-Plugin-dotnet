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
using com.IvanMurzak.ReflectorNet.Utils;

namespace com.IvanMurzak.McpPlugin
{
    public partial class McpPluginBuilder
    {
        private const BindingFlags MethodBindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        private void ProcessAllAssemblies()
        {
            // Collect all unique assemblies to scan once
            var allAssemblies = new HashSet<Assembly>();
            foreach (var assembly in _toolAssemblies)
                allAssemblies.Add(assembly);
            foreach (var assembly in _promptAssemblies)
                allAssemblies.Add(assembly);
            foreach (var assembly in _resourceAssemblies)
                allAssemblies.Add(assembly);

            // Track which assemblies are registered for which purpose
            var toolAssemblySet = new HashSet<Assembly>(_toolAssemblies);
            var promptAssemblySet = new HashSet<Assembly>(_promptAssemblies);
            var resourceAssemblySet = new HashSet<Assembly>(_resourceAssemblies);

            // Process explicitly registered types (combined scan)
            ProcessExplicitTypes();

            // Scan each assembly once, process types for all registered purposes
            foreach (var assembly in allAssemblies)
            {
                if (_ignoreConfig.IsIgnored(assembly))
                    continue;

                var isToolAssembly = toolAssemblySet.Contains(assembly);
                var isPromptAssembly = promptAssemblySet.Contains(assembly);
                var isResourceAssembly = resourceAssemblySet.Contains(assembly);

                foreach (var type in AssemblyUtils.GetAssemblyTypes(assembly))
                {
                    if (_ignoreConfig.IsIgnored(type))
                        continue;

                    // Use IsDefined for fast check before GetCustomAttribute
                    var hasToolTypeAttr = isToolAssembly && Attribute.IsDefined(type, typeof(McpPluginToolTypeAttribute));
                    var hasPromptTypeAttr = isPromptAssembly && Attribute.IsDefined(type, typeof(McpPluginPromptTypeAttribute));
                    var hasResourceTypeAttr = isResourceAssembly && Attribute.IsDefined(type, typeof(McpPluginResourceTypeAttribute));

                    if (!hasToolTypeAttr && !hasPromptTypeAttr && !hasResourceTypeAttr)
                        continue;

                    // Scan methods once, extract all attributes
                    ProcessTypeMethods(type, hasToolTypeAttr, hasPromptTypeAttr, hasResourceTypeAttr);
                }
            }
        }

        private void ProcessExplicitTypes()
        {
            // Convert to HashSet for O(1) lookup
            var promptTypeSet = new HashSet<Type>(_promptTypes);
            var resourceTypeSet = new HashSet<Type>(_resourceTypes);
            var processedTypes = new HashSet<Type>();

            foreach (var type in _toolTypes)
            {
                if (_ignoreConfig.IsIgnored(type) || !processedTypes.Add(type))
                    continue;

                // O(1) lookup with HashSet
                var inPromptTypes = promptTypeSet.Contains(type);
                var inResourceTypes = resourceTypeSet.Contains(type);

                ProcessTypeMethods(type, processTool: true, processPrompt: inPromptTypes, processResource: inResourceTypes);
            }

            foreach (var type in _promptTypes)
            {
                if (_ignoreConfig.IsIgnored(type) || processedTypes.Contains(type))
                    continue;

                processedTypes.Add(type);
                var inResourceTypes = resourceTypeSet.Contains(type);

                ProcessTypeMethods(type, processTool: false, processPrompt: true, processResource: inResourceTypes);
            }

            foreach (var type in _resourceTypes)
            {
                if (_ignoreConfig.IsIgnored(type) || processedTypes.Contains(type))
                    continue;

                ProcessTypeMethods(type, processTool: false, processPrompt: false, processResource: true);
            }
        }

        private void ProcessTypeMethods(Type type, bool processTool, bool processPrompt, bool processResource)
        {
            foreach (var method in type.GetMethods(MethodBindingFlags))
            {
                // Batch: get all custom attributes once per method
                var attributes = method.GetCustomAttributes(inherit: false);

                // Single pass through attributes
                foreach (var attr in attributes)
                {
                    if (processTool && attr is McpPluginToolAttribute toolAttr)
                    {
                        if (string.IsNullOrEmpty(toolAttr.Name))
                            throw new ArgumentException($"Tool name cannot be null or empty. Type: {type.Name}, Method: {method.Name}");
                        WithTool(toolAttr, classType: type, methodInfo: method);
                    }
                    else if (processPrompt && attr is McpPluginPromptAttribute promptAttr)
                    {
                        if (string.IsNullOrEmpty(promptAttr.Name))
                            throw new ArgumentException($"Prompt name cannot be null or empty. Type: {type.Name}, Method: {method.Name}");
                        WithPrompt(name: promptAttr.Name, classType: type, methodInfo: method);
                    }
                    else if (processResource && attr is McpPluginResourceAttribute resourceAttr)
                    {
                        WithResource(classType: type, getContentMethod: method);
                    }
                }
            }
        }
    }
}
