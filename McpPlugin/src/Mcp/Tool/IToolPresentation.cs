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
using System.Globalization;
using System.Text;
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

    /// <summary>How text written for the agent escapes characters inside quoted strings.</summary>
    public enum ToolTextEscaping
    {
        /// <summary>Only <c>"</c>, <c>\</c>, control characters and lone surrogates; <c>+</c>, <c>&lt;</c>, non-ASCII stay readable.</summary>
        Minimal,
        /// <summary>Minimal plus every non-ASCII character as <c>\uXXXX</c> (for providers that mangle non-ASCII text).</summary>
        Ascii,
        /// <summary>System.Text.Json's default escaping (also <c>+ &lt; &gt; &amp; '</c> and non-ASCII), as plain JSON results have.</summary>
        Json,
    }

    /// <summary>EXPERIMENTAL. The active <see cref="IToolPresentation"/>; null = the standard JSON presentation.</summary>
    public static class ToolPresentation
    {
        public static IToolPresentation? Current { get; set; }

        /// <summary>Escaping used by <see cref="Quote"/>; hosts set it from their configuration.</summary>
        public static ToolTextEscaping Escaping { get; set; } = ToolTextEscaping.Minimal;

        /// <summary><paramref name="text"/> as a double-quoted string literal escaped per <see cref="Escaping"/>.</summary>
        public static string Quote(string text) => Quote(text, Escaping);

        public static string Quote(string text, ToolTextEscaping escaping)
        {
            if (escaping == ToolTextEscaping.Json) return JsonSerializer.Serialize(text);
            var sb = new StringBuilder(text.Length + 2).Append('"');
            for (int i = 0; i < text.Length; i++)
            {
                var c = text[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        var pair = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
                        var lone = char.IsSurrogate(c) && !pair && !(char.IsLowSurrogate(c) && i > 0 && char.IsHighSurrogate(text[i - 1]));
                        if (c < ' ' || lone || (escaping == ToolTextEscaping.Ascii && c > '~'))
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
