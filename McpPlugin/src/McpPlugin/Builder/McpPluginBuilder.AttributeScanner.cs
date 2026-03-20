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
        private const BindingFlags FieldBindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

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
            foreach (var assembly in _skillAssemblies)
                allAssemblies.Add(assembly);

            // Track which assemblies are registered for which purpose
            var toolAssemblySet = new HashSet<Assembly>(_toolAssemblies);
            var promptAssemblySet = new HashSet<Assembly>(_promptAssemblies);
            var resourceAssemblySet = new HashSet<Assembly>(_resourceAssemblies);
            var skillAssemblySet = new HashSet<Assembly>(_skillAssemblies);

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
                var isSkillAssembly = skillAssemblySet.Contains(assembly);

                foreach (var type in AssemblyUtils.GetAssemblyTypes(assembly))
                {
                    if (_ignoreConfig.IsIgnored(type))
                        continue;

                    // Use IsDefined for fast check before GetCustomAttribute
                    var hasToolTypeAttr = isToolAssembly && Attribute.IsDefined(type, typeof(McpPluginToolTypeAttribute));
                    var hasPromptTypeAttr = isPromptAssembly && Attribute.IsDefined(type, typeof(McpPluginPromptTypeAttribute));
                    var hasResourceTypeAttr = isResourceAssembly && Attribute.IsDefined(type, typeof(McpPluginResourceTypeAttribute));
                    var hasSkillTypeAttr = isSkillAssembly && Attribute.IsDefined(type, typeof(McpPluginSkillTypeAttribute));

                    if (!hasToolTypeAttr && !hasPromptTypeAttr && !hasResourceTypeAttr && !hasSkillTypeAttr)
                        continue;

                    // Scan methods once, extract all attributes
                    ProcessTypeMethods(type, hasToolTypeAttr, hasPromptTypeAttr, hasResourceTypeAttr);

                    if (hasSkillTypeAttr)
                        ProcessTypeMembers(type);
                }
            }
        }

        private void ProcessExplicitTypes()
        {
            // Convert to HashSet for O(1) lookup
            var promptTypeSet = new HashSet<Type>(_promptTypes);
            var resourceTypeSet = new HashSet<Type>(_resourceTypes);
            var skillTypeSet = new HashSet<Type>(_skillTypes);
            var processedTypes = new HashSet<Type>();
            var processedSkillTypes = new HashSet<Type>();

            foreach (var type in _toolTypes)
            {
                if (_ignoreConfig.IsIgnored(type) || !processedTypes.Add(type))
                    continue;

                // O(1) lookup with HashSet
                var inPromptTypes = promptTypeSet.Contains(type);
                var inResourceTypes = resourceTypeSet.Contains(type);

                ProcessTypeMethods(type, processTool: true, processPrompt: inPromptTypes, processResource: inResourceTypes);

                if (skillTypeSet.Contains(type) && processedSkillTypes.Add(type))
                    ProcessTypeMembers(type);
            }

            foreach (var type in _promptTypes)
            {
                if (_ignoreConfig.IsIgnored(type) || processedTypes.Contains(type))
                    continue;

                processedTypes.Add(type);
                var inResourceTypes = resourceTypeSet.Contains(type);

                ProcessTypeMethods(type, processTool: false, processPrompt: true, processResource: inResourceTypes);

                if (skillTypeSet.Contains(type) && processedSkillTypes.Add(type))
                    ProcessTypeMembers(type);
            }

            foreach (var type in _resourceTypes)
            {
                if (_ignoreConfig.IsIgnored(type) || processedTypes.Contains(type))
                    continue;

                processedTypes.Add(type);
                ProcessTypeMethods(type, processTool: false, processPrompt: false, processResource: true);

                if (skillTypeSet.Contains(type) && processedSkillTypes.Add(type))
                    ProcessTypeMembers(type);
            }

            foreach (var type in _skillTypes)
            {
                if (_ignoreConfig.IsIgnored(type) || !processedSkillTypes.Add(type))
                    continue;

                ProcessTypeMembers(type);
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
                    else if (processResource && attr is McpPluginResourceAttribute)
                    {
                        WithResource(classType: type, getContentMethod: method);
                    }
                }
            }
        }

        private void ProcessTypeMembers(Type type)
        {
            // Scan const string fields
            foreach (var field in type.GetFields(FieldBindingFlags))
            {
                var skillAttr = field.GetCustomAttribute<McpPluginSkillAttribute>();
                if (skillAttr == null)
                    continue;

                if (!field.IsLiteral || field.FieldType != typeof(string))
                    throw new ArgumentException(
                        $"Field '{field.Name}' in type '{type.Name}' has [McpPluginSkill] but is not a const string. " +
                        "Only const string fields and static string properties are supported.");

                if (string.IsNullOrEmpty(skillAttr.Name))
                    throw new ArgumentException(
                        $"Skill name cannot be null or empty. Type: {type.Name}, Field: {field.Name}");

                var rawValue = field.GetRawConstantValue();
                if (rawValue == null)
                    throw new ArgumentException(
                        $"Skill field '{field.Name}' in type '{type.Name}' has a null constant value. " +
                        "Only non-null const string values are supported.");
                var content = (string)rawValue;

                _skillFields.Add(new SkillMemberData(
                    classType: type,
                    memberInfo: field,
                    attribute: skillAttr,
                    content: content
                ));
            }

            // Scan static string properties
            foreach (var property in type.GetProperties(FieldBindingFlags))
            {
                var skillAttr = property.GetCustomAttribute<McpPluginSkillAttribute>();
                if (skillAttr == null)
                    continue;

                if (property.PropertyType != typeof(string))
                    throw new ArgumentException(
                        $"Property '{property.Name}' in type '{type.Name}' has [McpPluginSkill] but is not a string property. " +
                        "Only const string fields and static string properties are supported.");

                var getter = property.GetGetMethod(nonPublic: true);
                if (getter == null || !getter.IsStatic)
                    throw new ArgumentException(
                        $"Property '{property.Name}' in type '{type.Name}' has [McpPluginSkill] but is not a static property with a getter. " +
                        "Only const string fields and static string properties are supported.");

                if (string.IsNullOrEmpty(skillAttr.Name))
                    throw new ArgumentException(
                        $"Skill name cannot be null or empty. Type: {type.Name}, Property: {property.Name}");

                var value = (string?)property.GetValue(null);
                if (value == null)
                    throw new ArgumentException(
                        $"Skill property '{property.Name}' in type '{type.Name}' returned null. " +
                        "Only non-null string values are supported.");

                _skillFields.Add(new SkillMemberData(
                    classType: type,
                    memberInfo: property,
                    attribute: skillAttr,
                    content: value
                ));
            }
        }
    }
}
