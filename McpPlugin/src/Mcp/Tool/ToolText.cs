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
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace com.IvanMurzak.McpPlugin
{
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

    /// <summary>Helpers for agent-facing text that an <see cref="IToolBinding"/> writes.</summary>
    public static class ToolText
    {
        /// <summary><paramref name="text"/> as a double-quoted string literal escaped per <paramref name="escaping"/>.</summary>
        public static string Quote(string text, ToolTextEscaping escaping)
        {
            if (escaping == ToolTextEscaping.Json) return JsonSerializer.Serialize(text);
            if (!NeedsEscape(text, escaping)) return "\"" + text + "\"";
            var sb = new StringBuilder(text.Length + 8).Append('"');
            for (int i = 0; i < text.Length; i++)
            {
                var c = text[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); continue;
                    case '\\': sb.Append("\\\\"); continue;
                    case '\n': sb.Append("\\n"); continue;
                    case '\r': sb.Append("\\r"); continue;
                    case '\t': sb.Append("\\t"); continue;
                }
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    // A valid pair: kept as is, or both halves escaped in Ascii mode.
                    if (escaping == ToolTextEscaping.Ascii) { Escape(sb, c); Escape(sb, text[++i]); }
                    else sb.Append(c).Append(text[++i]);
                    continue;
                }
                if (c < ' ' || char.IsSurrogate(c) || (escaping == ToolTextEscaping.Ascii && c > '~')) Escape(sb, c);
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        private static bool NeedsEscape(string text, ToolTextEscaping escaping)
        {
            foreach (var c in text)
                if (c == '"' || c == '\\' || c < ' ' || char.IsSurrogate(c) || (escaping == ToolTextEscaping.Ascii && c > '~'))
                    return true;
            return false;
        }

        private static void Escape(StringBuilder sb, char c) => sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
    }
}
