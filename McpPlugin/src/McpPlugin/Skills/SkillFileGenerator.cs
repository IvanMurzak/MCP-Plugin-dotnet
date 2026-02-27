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
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Skills
{
    /// <summary>
    /// Generates AI skill markdown files for each registered MCP tool.
    /// Skill files follow the AI Skills template format and describe how to call each tool
    /// via the direct HTTP API or MCP protocol, including JSON schemas for input and output.
    /// </summary>
    public class SkillFileGenerator
    {
        readonly ILogger? _logger;

        static readonly JsonSerializerOptions _prettyJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public SkillFileGenerator(ILogger? logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// Generates skill markdown files for all provided tools.
        /// <paramref name="skillsPath"/> must be an absolute path; callers are responsible for resolving
        /// relative paths before calling this method.
        /// Provide <paramref name="host"/> to include correct API endpoint URLs in the generated markdown.
        /// </summary>
        public bool Generate(IEnumerable<IRunTool> tools, string skillsPath, string host)
        {
            if (tools == null)
            {
                _logger?.LogWarning("{class}.{method}: tools collection is null, skipping.", nameof(SkillFileGenerator), nameof(Generate));
                return false;
            }

            var skillsDir = skillsPath;

            try
            {
                Directory.CreateDirectory(skillsDir);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "{class}.{method}: Failed to create skills directory '{dir}'.",
                    nameof(SkillFileGenerator), nameof(Generate), skillsDir);
                return false;
            }

            var toolList = new List<IRunTool>();
            foreach (var tool in tools)
                if (tool != null) toolList.Add(tool);

            var nameMap = BuildNameMap(toolList, nameof(Generate));
            foreach (var tool in toolList)
                GenerateFor(tool, skillsDir, host, nameMap[tool.Name]);

            return true;
        }

        /// <summary>
        /// Deletes the skill subdirectory for each tool in <paramref name="tools"/> from
        /// <paramref name="skillsPath"/>. Only the subdirectories that correspond to the provided
        /// tools are removed; all other content inside <paramref name="skillsPath"/> is left intact.
        /// <paramref name="skillsPath"/> must be an absolute path; callers are responsible for resolving
        /// relative paths before calling this method.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> if the operation completed without errors;
        /// <see langword="false"/> if <paramref name="tools"/> is <see langword="null"/> or a deletion failed.
        /// </returns>
        public bool Delete(IEnumerable<IRunTool> tools, string skillsPath)
        {
            if (tools == null)
            {
                _logger?.LogWarning("{class}.{method}: tools collection is null, skipping.", nameof(SkillFileGenerator), nameof(Delete));
                return false;
            }

            var skillsDir = skillsPath;

            if (!Directory.Exists(skillsDir))
                return true;

            var toolList = new List<IRunTool>();
            foreach (var tool in tools)
                if (tool != null) toolList.Add(tool);

            var nameMap = BuildNameMap(toolList, nameof(Delete));

            var success = true;
            foreach (var tool in toolList)
            {
                var skillDir = Path.Combine(skillsDir, nameMap[tool.Name]);
                if (!Directory.Exists(skillDir))
                    continue;

                try
                {
                    Directory.Delete(skillDir, recursive: true);
                    _logger?.LogDebug("{class}.{method}: Deleted skill directory for tool '{tool}' → '{path}'.",
                        nameof(SkillFileGenerator), nameof(Delete), tool.Name, skillDir);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "{class}.{method}: Failed to delete skill directory for tool '{tool}' at '{path}'.",
                        nameof(SkillFileGenerator), nameof(Delete), tool.Name, skillDir);
                    success = false;
                }
            }

            return success;
        }

        /// <summary>
        /// Builds a mapping from each tool's raw name to its final skill directory name,
        /// applying <see cref="SanitizeSkillName"/> and appending a stable hash suffix when two or
        /// more tools sanitize to the same string (collision handling).
        /// </summary>
        Dictionary<string, string> BuildNameMap(List<IRunTool> tools, string callerName)
        {
            // Group by sanitized name to detect collisions (e.g. "foo bar" and "foo-bar" both → "foo-bar")
            var sanitizedGroups = new Dictionary<string, List<IRunTool>>(StringComparer.Ordinal);
            foreach (var tool in tools)
            {
                var sanitized = SanitizeSkillName(tool.Name);
                if (!sanitizedGroups.TryGetValue(sanitized, out var group))
                    sanitizedGroups[sanitized] = group = new List<IRunTool>();
                group.Add(tool);
            }

            // Build final skill-directory names, appending a stable hash suffix on any collision
            var nameMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kvp in sanitizedGroups)
            {
                if (kvp.Value.Count == 1)
                {
                    nameMap[kvp.Value[0].Name] = kvp.Key;
                }
                else
                {
                    _logger?.LogWarning(
                        "{class}.{method}: Tools [{tools}] all sanitize to '{sanitized}'. Appending hash suffixes to avoid directory collisions.",
                        nameof(SkillFileGenerator), callerName,
                        string.Join(", ", kvp.Value.ConvertAll(t => "'" + t.Name + "'")),
                        kvp.Key);

                    foreach (var tool in kvp.Value)
                        nameMap[tool.Name] = kvp.Key + "-" + StableShortHash(tool.Name);
                }
            }

            return nameMap;
        }

        /// <summary>
        /// Generates a skill subdirectory and SKILL.md file for the given tool inside <paramref name="skillsDir"/>.
        /// Each skill gets its own subdirectory named after the sanitized tool name, containing SKILL.md.
        /// </summary>
        void GenerateFor(IRunTool tool, string skillsDir, string host, string skillName)
        {
            var skillDir = Path.Combine(skillsDir, skillName);
            var filePath = Path.Combine(skillDir, "SKILL.md");

            try
            {
                Directory.CreateDirectory(skillDir);
                var content = BuildMarkdown(tool, skillName, host);
                File.WriteAllText(filePath, content, Encoding.UTF8);
                _logger?.LogDebug("{class}.{method}: Skill file written for tool '{tool}' → '{path}'.",
                    nameof(SkillFileGenerator), nameof(GenerateFor), tool.Name, filePath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "{class}.{method}: Failed to write skill file for tool '{tool}' at '{path}'.",
                    nameof(SkillFileGenerator), nameof(GenerateFor), tool.Name, filePath);
            }
        }

        string BuildMarkdown(IRunTool tool, string skillName, string host)
        {
            var sb = new StringBuilder();
            var title = tool.Title ?? tool.Name;
            var description = tool.Description ?? string.Empty;

            // YAML front-matter
            sb.AppendLine("---");
            sb.AppendLine($"name: {EscapeYaml(skillName)}");
            sb.AppendLine($"description: {EscapeYaml(description)}");
            sb.AppendLine("---");
            sb.AppendLine();

            // Title
            sb.AppendLine($"# {title}");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(description))
            {
                sb.AppendLine(description);
                sb.AppendLine();
            }

            // How to Call section
            sb.AppendLine("## How to Call");
            sb.AppendLine();
            sb.AppendLine("### HTTP API (Direct Tool Execution)");
            sb.AppendLine();
            sb.AppendLine("Execute this tool directly via the MCP Plugin HTTP API:");
            sb.AppendLine();

            var inputExample = BuildInputExample(tool.InputSchema);
            sb.AppendLine("```bash");
            sb.AppendLine($"curl -X POST {host}/api/tools/{tool.Name} \\");
            sb.AppendLine("  -H \"Content-Type: application/json\" \\");
            sb.AppendLine($"  -d '{inputExample}'");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("#### With Authorization (if required)");
            sb.AppendLine();
            sb.AppendLine("```bash");
            sb.AppendLine($"curl -X POST {host}/api/tools/{tool.Name} \\");
            sb.AppendLine("  -H \"Content-Type: application/json\" \\");
            sb.AppendLine("  -H \"Authorization: Bearer YOUR_TOKEN\" \\");
            sb.AppendLine($"  -d '{inputExample}'");
            sb.AppendLine("```");
            sb.AppendLine();

            // Input section
            sb.AppendLine("## Input");
            sb.AppendLine();
            AppendParameterTable(sb, tool.InputSchema);
            sb.AppendLine();
            sb.AppendLine("### Input JSON Schema");
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.AppendLine(PrettyPrintJson(tool.InputSchema));
            sb.AppendLine("```");
            sb.AppendLine();

            // Output section
            sb.AppendLine("## Output");
            sb.AppendLine();
            if (tool.OutputSchema != null)
            {
                sb.AppendLine("### Output JSON Schema");
                sb.AppendLine();
                sb.AppendLine("```json");
                sb.AppendLine(PrettyPrintJson(tool.OutputSchema));
                sb.AppendLine("```");
            }
            else
            {
                sb.AppendLine("This tool does not return structured output.");
            }

            return sb.ToString();
        }

        void AppendParameterTable(StringBuilder sb, JsonNode? inputSchema)
        {
            if (inputSchema == null)
            {
                sb.AppendLine("This tool takes no input parameters.");
                return;
            }

            var properties = inputSchema["properties"] as JsonObject;
            if (properties == null || properties.Count == 0)
            {
                sb.AppendLine("This tool takes no input parameters.");
                return;
            }

            var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var requiredArray = inputSchema["required"] as JsonArray;
            if (requiredArray != null)
            {
                foreach (var item in requiredArray)
                    if (item?.GetValue<string>() is string r)
                        required.Add(r);
            }

            sb.AppendLine("| Name | Type | Required | Description |");
            sb.AppendLine("|------|------|----------|-------------|");

            foreach (var prop in properties)
            {
                var propName = prop.Key;
                var propNode = prop.Value as JsonObject;
                var propType = propNode?["type"]?.GetValue<string>() ?? "any";
                var propDesc = propNode?["description"]?.GetValue<string>() ?? string.Empty;
                var isRequired = required.Contains(propName) ? "Yes" : "No";

                sb.AppendLine($"| `{propName}` | `{propType}` | {isRequired} | {propDesc} |");
            }
        }

        string BuildInputExample(JsonNode? inputSchema)
        {
            if (inputSchema == null)
                return "{}";

            var properties = inputSchema["properties"] as JsonObject;
            if (properties == null || properties.Count == 0)
                return "{}";

            var example = new JsonObject();
            foreach (var prop in properties)
            {
                var propNode = prop.Value as JsonObject;
                var propType = propNode?["type"]?.GetValue<string>() ?? "string";
                example[prop.Key] = CreateExampleValue(propType, propNode);
            }

            return example.ToJsonString(_prettyJsonOptions);
        }

        static JsonNode CreateExampleValue(string type, JsonObject? schema)
        {
            return type switch
            {
                "integer" => JsonValue.Create(0)!,
                "number" => JsonValue.Create(0.0)!,
                "boolean" => JsonValue.Create(false)!,
                "array" => new JsonArray(),
                "object" => new JsonObject(),
                "null" => JsonValue.Create((string?)null)!,
                _ => JsonValue.Create("string_value")!
            };
        }

        string PrettyPrintJson(JsonNode? node)
        {
            if (node == null)
                return "null";
            try
            {
                return node.ToJsonString(_prettyJsonOptions);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "{class}.{method}: Failed to pretty-print JSON schema.", nameof(SkillFileGenerator), nameof(PrettyPrintJson));
                return node.ToString();
            }
        }

        /// <summary>
        /// Converts a tool name into a valid Agent Skills directory/name:
        /// lowercase alphanumeric and hyphens only, no leading/trailing/consecutive hyphens.
        /// </summary>
        static string SanitizeSkillName(string name)
        {
            var sb = new StringBuilder();
            bool lastWasHyphen = false;

            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                    lastWasHyphen = false;
                }
                else if (sb.Length > 0 && !lastWasHyphen)
                {
                    sb.Append('-');
                    lastWasHyphen = true;
                }
            }

            while (sb.Length > 0 && sb[sb.Length - 1] == '-')
                sb.Length--;

            return sb.Length > 0 ? sb.ToString() : "tool-" + StableShortHash(name);
        }

        /// <summary>
        /// Returns a stable 4-character lowercase hex string derived from <paramref name="value"/>
        /// using FNV-1a 32-bit over the UTF-8 byte representation, so the suffix is consistent
        /// across runs and runtimes and handles the full Unicode range correctly.
        /// </summary>
        static string StableShortHash(string value)
        {
            uint hash = 2166136261u;
            foreach (byte b in Encoding.UTF8.GetBytes(value))
            {
                hash ^= b;
                hash *= 16777619u;
            }
            return (hash & 0xFFFFu).ToString("x4");
        }

        static string EscapeYaml(string value)
        {
            if (value.Contains(':') || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\\\"")}\"";
            return value;
        }
    }
}
