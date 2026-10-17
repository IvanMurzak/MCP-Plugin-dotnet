/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Text.RegularExpressions;

namespace com.IvanMurzak.McpPlugin.Common
{
    /// <summary>
    /// Format rules for an engine identifier. The engine is an opaque label supplied by the plugin host;
    /// this library never decides which engines exist (the authorization server owns that), it only
    /// normalises the format so a malformed value never reaches the wire.
    /// </summary>
    public static class EngineId
    {
        /// <summary>Sent in place of an empty or malformed engine identifier.</summary>
        public const string Unknown = "unknown";

        /// <summary>
        /// The accepted format: a lower-case slug of 1-32 characters (<c>a-z</c>, <c>0-9</c>, <c>.</c>, <c>_</c>,
        /// <c>-</c>) starting with a letter or digit.
        /// </summary>
        public const string Pattern = "^[a-z0-9][a-z0-9._-]{0,31}$";

        /// <summary>Human-readable description of <see cref="Pattern"/>, for error messages.</summary>
        public const string FormatDescription =
            "a lower-case identifier of 1-32 characters (letters a-z, digits, '.', '_' or '-') starting with a letter or digit";

        static readonly Regex Regex = new Regex(Pattern, RegexOptions.CultureInvariant);

        /// <summary>True when <paramref name="engine"/> already matches <see cref="Pattern"/> exactly (no trimming or case folding).</summary>
        public static bool IsValid(string? engine)
            => !string.IsNullOrEmpty(engine) && engine![engine.Length - 1] != '\n' && Regex.IsMatch(engine); // `$` also matches before a final '\n'

        /// <summary>Trims and lower-cases <paramref name="engine"/> (invariant culture); returns it when it matches
        /// <see cref="Pattern"/>, else <see cref="Unknown"/>.</summary>
        public static string Normalize(string? engine)
        {
            var e = (engine ?? string.Empty).Trim().ToLowerInvariant();
            return IsValid(e) ? e : Unknown;
        }
    }
}
