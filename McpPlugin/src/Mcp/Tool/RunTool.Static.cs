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
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// Provides functionality to execute methods dynamically, supporting both static and instance methods.
    /// Allows for parameter passing by position or by name, with support for default parameter values.
    /// </summary>
    public partial class RunTool : MethodWrapper, IRunTool
    {
        /// <summary>
        /// Initializes the Command with the target static method information.
        /// </summary>
        /// <param name="reflector">The Reflector instance used for method invocation.</param>
        /// <param name="logger">The logger for logging execution details (optional).</param>
        /// <param name="methodInfo">The MethodInfo of the static method to execute.</param>
        /// <param name="title">An optional title for the command.</param>
        public static RunTool CreateFromStaticMethod(Reflector reflector, ILogger? logger, MethodInfo methodInfo, string? title = null)
            => new RunTool(reflector, logger, methodInfo)
            {
                Title = title
            };

        /// <summary>
        /// Initializes the Command with the target instance method information.
        /// </summary>
        /// <param name="targetInstance">The instance of the object containing the method.</param>
        /// <param name="methodInfo">The MethodInfo of the instance method to execute.</param>
        public static RunTool CreateFromInstanceMethod(Reflector reflector, ILogger? logger, object targetInstance, MethodInfo methodInfo, string? title = null)
            => new RunTool(reflector, logger, targetInstance, methodInfo)
            {
                Title = title
            };

        /// <summary>
        /// Initializes the Command with the target method information for a specified class type.
        /// </summary>
        /// <param name="reflector">The reflector used for method invocation and analysis.</param>
        /// <param name="logger">The logger for diagnostic and runtime information.</param>
        /// <param name="classType">The type containing the method to execute.</param>
        /// <param name="methodInfo">The MethodInfo of the method to execute.</param>
        /// <param name="title">An optional title for the command.</param>
        public static RunTool CreateFromClassMethod(Reflector reflector, ILogger? logger, Type classType, MethodInfo methodInfo, string? title = null)
            => new RunTool(reflector, logger, classType, methodInfo)
            {
                Title = title
            };
    }
}
