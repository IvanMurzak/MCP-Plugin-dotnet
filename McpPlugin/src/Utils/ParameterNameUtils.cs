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

namespace com.IvanMurzak.McpPlugin.Utils
{
    /// <summary>
    /// Provides utility methods for case-insensitive parameter name normalization.
    /// Used to match LLM-provided input arguments with method parameter names regardless of casing.
    /// </summary>
    public static class ParameterNameUtils
    {
        /// <summary>
        /// Builds a lookup dictionary for case-insensitive parameter name matching.
        /// Maps lowercase parameter names to their actual names. Only includes entries
        /// where there's no case conflict (i.e., no two parameters differ only by case).
        /// </summary>
        /// <param name="methodParams">The method parameters to build the lookup from.</param>
        /// <returns>A dictionary mapping lowercase names to actual parameter names, or null if no valid mappings exist.</returns>
        public static Dictionary<string, string>? BuildParameterNameLookup(ParameterInfo[]? methodParams)
        {
            if (methodParams == null || methodParams.Length == 0)
                return null;

            var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
            var conflicts = new HashSet<string>(StringComparer.Ordinal);

            foreach (var param in methodParams)
            {
                if (param.Name == null)
                    continue;

                var lowerName = param.Name.ToLowerInvariant();

                // Check if this lowercase name already exists (conflict)
                if (lookup.ContainsKey(lowerName))
                {
                    // Mark as conflict - don't allow case-insensitive matching for this name
                    conflicts.Add(lowerName);
                }
                else
                {
                    lookup[lowerName] = param.Name;
                }
            }

            // Remove conflicting entries - when two parameters differ only by case,
            // we can't safely normalize, so we remove both from the lookup
            foreach (var conflict in conflicts)
            {
                lookup.Remove(conflict);
            }

            return lookup.Count > 0 ? lookup : null;
        }

        /// <summary>
        /// Normalizes the parameter name using case-insensitive matching.
        /// If the incoming name matches an expected parameter name (case-insensitive) and there's no conflict,
        /// returns the expected parameter name. Otherwise, returns the original name.
        /// </summary>
        /// <param name="incomingName">The incoming parameter name from the request.</param>
        /// <param name="paramNameLookup">The lookup dictionary for case-insensitive matching.</param>
        /// <returns>The normalized parameter name.</returns>
        public static string NormalizeParameterName(string incomingName, Dictionary<string, string>? paramNameLookup)
        {
            if (paramNameLookup == null || string.IsNullOrEmpty(incomingName))
                return incomingName;

            var lowerIncoming = incomingName.ToLowerInvariant();

            // If we have a case-insensitive match, use the actual parameter name
            if (paramNameLookup.TryGetValue(lowerIncoming, out var actualName))
            {
                return actualName;
            }

            // No match found, return the original name
            return incomingName;
        }
    }
}
