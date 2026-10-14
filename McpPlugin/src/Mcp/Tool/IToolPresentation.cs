/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using com.IvanMurzak.McpPlugin.Common.Model;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// EXPERIMENTAL. Changes how tools are presented to the AI agent without changing the tools: the listed description and
    /// schemas, the arguments before they are deserialized, and the result before it is returned. The tool still sees the
    /// arguments its JSON schema describes, so ReflectorNet deserialization and the tool code are unchanged.
    /// </summary>
    public interface IToolPresentation
    {
        /// <summary>The listing entry the agent sees (description, input and output schema); edit it in place.</summary>
        void List(IRunTool tool, ResponseListTool listed);

        /// <summary>
        /// Turn the arguments the agent sent into the arguments the tool's own schema expects. Return an error message to
        /// reject the call without running the tool.
        /// </summary>
        string? Arguments(IRunTool tool, IReadOnlyDictionary<string, JsonElement> arguments, out IReadOnlyDictionary<string, JsonElement> converted);

        /// <summary>The result the agent receives; return <paramref name="result"/> itself to keep it.</summary>
        ResponseCallTool Result(IRunTool tool, ResponseCallTool result);
    }

    /// <summary>EXPERIMENTAL. The active <see cref="IToolPresentation"/>; null = the standard JSON presentation.</summary>
    public static class ToolPresentation
    {
        public static IToolPresentation? Current { get; set; }
    }
}
