/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// Factory helper that creates <see cref="IRunTool"/> instances from
    /// <see cref="ToolMethodData"/>, eliminating duplication between
    /// <see cref="ToolRunnerCollection"/> and <see cref="SystemToolRunnerCollection"/>.
    /// </summary>
    internal static class RunToolFactory
    {
        public static IRunTool Create(ToolMethodData method, Reflector reflector, ILogger? logger)
        {
            var attr = method.Attribute;

            return method.MethodInfo.IsStatic
                ? (IRunTool)RunTool.CreateFromStaticMethod(
                    reflector: reflector,
                    logger: logger,
                    name: attr.Name,
                    methodInfo: method.MethodInfo,
                    title: attr.Title,
                    readOnlyHint: attr.ReadOnlyHintValue,
                    destructiveHint: attr.DestructiveHintValue,
                    idempotentHint: attr.IdempotentHintValue,
                    openWorldHint: attr.OpenWorldHintValue,
                    enabled: attr.EnabledValue,
                    toolType: attr.ToolType)
                : RunTool.CreateFromClassMethod(
                    reflector: reflector,
                    logger: logger,
                    name: attr.Name,
                    classType: method.ClassType,
                    methodInfo: method.MethodInfo,
                    title: attr.Title,
                    readOnlyHint: attr.ReadOnlyHintValue,
                    destructiveHint: attr.DestructiveHintValue,
                    idempotentHint: attr.IdempotentHintValue,
                    openWorldHint: attr.OpenWorldHintValue,
                    enabled: attr.EnabledValue,
                    toolType: attr.ToolType);
        }
    }
}
