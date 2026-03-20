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
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Skills
{
    /// <summary>
    /// Generates AI skill markdown files for each registered MCP tool.
    /// Skill files follow the AI Skills template format and describe how to call each tool
    /// via the direct HTTP API or MCP protocol, including JSON schemas for input and output.
    /// <para>
    /// Override virtual members to customise any aspect of file generation without replacing
    /// the entire class. Register a custom subclass via
    /// <c>McpPluginBuilder.WithSkillFileGenerator&lt;T&gt;()</c>.
    /// </para>
    /// </summary>
    public class SkillFileGenerator : ISkillFileGenerator
    {
        readonly ILogger? _logger;

        static readonly JsonSerializerOptions _prettyJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public SkillFileGenerator(ILogger? logger = null)
        {
            _logger = logger;
        }

        // ── Customisation properties ─────────────────────────────────────────

        /// <summary>
        /// When <see langword="true"/> (default), a "With Authorization" curl block is included
        /// in the "How to Call" section of each SKILL.md.
        /// Override and return <see langword="false"/> to omit it.
        /// </summary>
        public virtual bool IncludeAuthorizationExample { get; } = true;

        /// <summary>
        /// When <see langword="true"/> (default), the parameter table is included in the
        /// "Input" section. Override and return <see langword="false"/> to omit it.
        /// </summary>
        public virtual bool IncludeParameterTable { get; } = true;

        /// <summary>
        /// When <see langword="true"/> (default), the description paragraph is rendered after the
        /// <c># Title</c> heading (duplicating the front-matter <c>description</c> field).
        /// Override and return <see langword="false"/> to omit it and save tokens — the
        /// description is still present in the YAML front-matter.
        /// </summary>
        public virtual bool IncludeDescriptionBody { get; } = true;

        /// <summary>
        /// When <see langword="true"/> (default), the raw "Input JSON Schema" code block is
        /// included after the parameter table. Override and return <see langword="false"/> to omit it.
        /// </summary>
        public virtual bool IncludeInputJsonSchema { get; } = true;

        /// <summary>
        /// When <see langword="true"/> (default), <c>description</c> fields inside
        /// <c>properties</c> are preserved in the Input JSON Schema block.
        /// Override and return <see langword="false"/> to strip them — useful when the
        /// parameter table already displays descriptions, saving tokens.
        /// Only affects top-level <c>properties</c>; <c>$defs</c> descriptions are untouched.
        /// </summary>
        public virtual bool IncludeInputSchemaPropertyDescriptions { get; } = true;

        /// <summary>
        /// When <see langword="true"/> (default), the "Output" section is included.
        /// Override and return <see langword="false"/> to omit the entire section.
        /// </summary>
        public virtual bool IncludeOutputSection { get; } = true;

        /// <summary>
        /// Controls where the text returned by <see cref="GetAdditionalContent"/> is injected
        /// into each SKILL.md. Defaults to <see cref="SkillAdditionalContentPosition.End"/>.
        /// Has no effect when <see cref="GetAdditionalContent"/> returns <see langword="null"/>
        /// or an empty string, or when the position is <see cref="SkillAdditionalContentPosition.None"/>.
        /// </summary>
        public virtual SkillAdditionalContentPosition AdditionalContentPosition { get; } = SkillAdditionalContentPosition.End;

        /// <summary>
        /// Returns additional markdown text to inject into the SKILL.md for <paramref name="tool"/>
        /// at the location specified by <see cref="AdditionalContentPosition"/>.
        /// The default implementation returns <see langword="null"/> (no injection).
        /// Override to supply custom content such as usage notes, links, or warnings.
        /// </summary>
        public virtual string? GetAdditionalContent(IRunTool tool) => null;

        // ── Interface implementation ─────────────────────────────────────────

        /// <summary>
        /// Generates skill markdown files for all provided tools.
        /// <paramref name="skillsPath"/> may be an absolute or relative path; relative paths are resolved
        /// against the current working directory at the time of the call.
        /// Provide <paramref name="host"/> to include correct API endpoint URLs in the generated markdown.
        /// </summary>
        public virtual bool Generate(IEnumerable<IRunTool> tools, string skillsPath, string host)
        {
            if (tools == null)
            {
                _logger?.LogWarning("{class}.{method}: tools collection is null, skipping.",
                    nameof(SkillFileGenerator), nameof(Generate));
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
            var success = true;
            foreach (var tool in toolList)
                if (!GenerateFor(tool, skillsDir, host, nameMap[tool.Name]))
                    success = false;

            return success;
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
        public virtual bool Delete(IEnumerable<IRunTool> tools, string skillsPath)
        {
            if (tools == null)
            {
                _logger?.LogWarning("{class}.{method}: tools collection is null, skipping.",
                    nameof(SkillFileGenerator), nameof(Delete));
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

        // ── Protected virtual helpers ────────────────────────────────────────

        /// <summary>
        /// Builds a mapping from each tool's raw name to its final skill directory name,
        /// applying <see cref="SanitizeSkillName"/> and appending a stable hash suffix when two or
        /// more tools sanitize to the same string (collision handling).
        /// </summary>
        protected virtual Dictionary<string, string> BuildNameMap(List<IRunTool> tools, string callerName)
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
        protected virtual bool GenerateFor(IRunTool tool, string skillsDir, string host, string skillName)
        {
            skillName = SanitizeSkillName(skillName);
            var skillDir = Path.Combine(skillsDir, skillName);
            var filePath = Path.Combine(skillDir, "SKILL.md");

            try
            {
                Directory.CreateDirectory(skillDir);
                var content = BuildMarkdown(tool, skillName, host);
                File.WriteAllText(filePath, content, Encoding.UTF8);
                _logger?.LogDebug("{class}.{method}: Skill file written for tool '{tool}' → '{path}'.",
                    nameof(SkillFileGenerator), nameof(GenerateFor), tool.Name, filePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "{class}.{method}: Failed to write skill file for tool '{tool}' at '{path}'.",
                    nameof(SkillFileGenerator), nameof(GenerateFor), tool.Name, filePath);
                return false;
            }
        }

        /// <summary>
        /// Builds the full markdown content for a single tool's SKILL.md.
        /// Override to replace or extend the entire document structure.
        /// For targeted changes, prefer overriding individual section helpers or the
        /// customisation properties/methods instead.
        /// </summary>
        protected virtual string BuildMarkdown(IRunTool tool, string skillName, string host)
        {
            host = host.TrimEnd('/');
            var sb = new StringBuilder();
            var title = tool.Title ?? tool.Name;
            var description = tool.Description ?? string.Empty;
            var additionalContent = GetAdditionalContent(tool);

            // YAML front-matter
            sb.AppendLine("---");
            sb.AppendLine($"name: {EscapeYaml(skillName)}");
            sb.AppendLine($"description: {EscapeYaml(description)}");
            sb.AppendLine("---");
            sb.AppendLine();
            BuildFrontMatterNotes(sb);

            // Title
            sb.AppendLine($"# {title}");
            sb.AppendLine();
            if (IncludeDescriptionBody && !string.IsNullOrWhiteSpace(description))
            {
                sb.AppendLine(description);
                sb.AppendLine();
            }
            BuildDescriptionNotes(sb);

            AppendAdditionalContent(sb, additionalContent, SkillAdditionalContentPosition.AfterTitle);

            // How to Call section
            sb.AppendLine("## How to Call");
            sb.AppendLine();
            BuildHowToCallHeading(sb);
            BuildHowToCallIntroNotes(sb);

            var inputExample = BuildInputExample(tool.InputSchema);
            BuildToolCommand(sb, tool, host, inputExample);
            BuildInputExampleNotes(sb);

            if (IncludeAuthorizationExample)
            {
                BuildToolCommandWithAuth(sb, tool, host, inputExample);
                BuildInputAuthorizationNotes(sb);
            }

            AppendAdditionalContent(sb, additionalContent, SkillAdditionalContentPosition.AfterHowToCall);

            // Input section
            sb.AppendLine("## Input");
            sb.AppendLine();
            BuildInputSectionNotes(sb);

            if (IncludeParameterTable)
            {
                AppendParameterTable(sb, tool.InputSchema);
                sb.AppendLine();
                BuildParameterTableNotes(sb);
            }

            if (IncludeInputJsonSchema)
            {
                BuildInputJsonSchemaBlock(sb, tool);
                BuildInputJsonSchemaNotes(sb);
            }

            AppendAdditionalContent(sb, additionalContent, SkillAdditionalContentPosition.AfterInput);

            // Output section
            if (IncludeOutputSection)
            {
                sb.AppendLine("## Output");
                sb.AppendLine();
                BuildOutputSectionNotes(sb);
                BuildOutputSchemaBlock(sb, tool);
                BuildOutputSchemaNotes(sb);
                sb.AppendLine();
            }

            AppendAdditionalContent(sb, additionalContent, SkillAdditionalContentPosition.End);

            return sb.ToString();
        }

        /// <summary>
        /// Override to inject additional markdown content immediately after the closing <c>---</c>
        /// of the YAML front-matter block and before the <c># Title</c> heading.
        /// <para>
        /// Injected position in the generated document:
        /// <code>
        /// ---
        /// name: tool-name
        /// description: ...
        /// ---
        ///                          ← content appended HERE
        /// # Tool Title
        /// </code>
        /// </para>
        /// </summary>
        /// <param name="sb">The <see cref="StringBuilder"/> that accumulates the skill file content.</param>
        protected virtual void BuildFrontMatterNotes(StringBuilder sb)
        {
            // No notes by default; override to add content between front-matter and title.
        }

        /// <summary>
        /// Override to inject additional markdown content after the tool description paragraph
        /// and before the <c>## How to Call</c> section (including before any
        /// <see cref="SkillAdditionalContentPosition.AfterTitle"/> additional content).
        /// <para>
        /// Injected position in the generated document:
        /// <code>
        /// # Tool Title
        ///
        /// Tool description text.
        ///
        ///                          ← content appended HERE
        /// ## How to Call
        /// </code>
        /// </para>
        /// </summary>
        /// <param name="sb">The <see cref="StringBuilder"/> that accumulates the skill file content.</param>
        protected virtual void BuildDescriptionNotes(StringBuilder sb)
        {
            // No notes by default; override to add content after the description.
        }

        /// <summary>
        /// Override to replace the sub-heading and introductory text at the top of the
        /// <c>## How to Call</c> section.
        /// Called after <c>## How to Call</c> is written, before <see cref="BuildHowToCallIntroNotes"/>.
        /// <para>
        /// Default output:
        /// <code>
        /// ## How to Call
        ///
        ///                          ← this method writes HERE
        /// (BuildHowToCallIntroNotes)
        /// (BuildToolCommand)
        /// </code>
        /// </para>
        /// </summary>
        /// <param name="sb">The <see cref="StringBuilder"/> that accumulates the skill file content.</param>
        protected virtual void BuildHowToCallHeading(StringBuilder sb)
        {
            sb.AppendLine("### HTTP API (Direct Tool Execution)");
            sb.AppendLine();
            sb.AppendLine("Execute this tool directly via the MCP Plugin HTTP API:");
            sb.AppendLine();
        }

        /// <summary>
        /// Override to inject additional markdown content after <see cref="BuildHowToCallHeading"/>
        /// and before <see cref="BuildToolCommand"/> in the <c>## How to Call</c> section.
        /// <para>
        /// Injected position in the generated document:
        /// <code>
        /// ### HTTP API (Direct Tool Execution)
        ///
        /// Execute this tool directly via the MCP Plugin HTTP API:
        ///
        ///                          ← content appended HERE
        /// ```bash
        /// curl -X POST {host}/api/tools/{name} \
        /// ```
        /// </code>
        /// </para>
        /// </summary>
        /// <param name="sb">The <see cref="StringBuilder"/> that accumulates the skill file content.</param>
        protected virtual void BuildHowToCallIntroNotes(StringBuilder sb)
        {
            // No notes by default; override to add content before the tool command.
        }

        /// <summary>
        /// Override to replace the basic tool invocation command block in the
        /// <c>## How to Call</c> section. By default emits a <c>curl -X POST</c> snippet.
        /// Called after <see cref="BuildHowToCallIntroNotes"/>, before <see cref="BuildInputExampleNotes"/>.
        /// <para>
        /// Default output:
        /// <code>
        /// ```bash
        /// curl -X POST {host}/api/tools/{name} \
        ///   -H "Content-Type: application/json" \
        ///   -d '{inputExample}'
        /// ```
        /// </code>
        /// </para>
        /// </summary>
        /// <param name="sb">The <see cref="StringBuilder"/> that accumulates the skill file content.</param>
        /// <param name="tool">The tool being documented.</param>
        /// <param name="host">The server host URL (trailing slash already trimmed).</param>
        /// <param name="inputExample">Compact JSON example string produced by <see cref="BuildInputExample"/>.</param>
        protected virtual void BuildToolCommand(StringBuilder sb, IRunTool tool, string host, string inputExample)
        {
            sb.AppendLine("```bash");
            sb.AppendLine($"curl -X POST {host}{GetApiRoutePrefix(tool)}/{tool.Name} \\");
            sb.AppendLine("  -H \"Content-Type: application/json\" \\");
            sb.AppendLine($"  -d '{inputExample}'");
            sb.AppendLine("```");
            sb.AppendLine();
            AppendInputFileHint(sb, tool, host, inputExample);
        }

        /// <summary>
        /// Appends a short hint about using <c>--input-file</c> (or <c>-d @file</c>) for complex input.
        /// Only emitted when the input example is non-trivial (not empty <c>{}</c>).
        /// Override to suppress or customise the hint.
        /// </summary>
        protected virtual void AppendInputFileHint(StringBuilder sb, IRunTool tool, string host, string inputExample)
        {
            if (inputExample == "{}")
                return;
            sb.AppendLine("> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.");
            sb.AppendLine(">");
            sb.AppendLine("> Or pipe via stdin:");
            sb.AppendLine("> ```bash");
            sb.AppendLine($"> curl -X POST {host}{GetApiRoutePrefix(tool)}/{tool.Name} -H \"Content-Type: application/json\" -d @- <<'EOF'");
            sb.AppendLine("> {\"param\": \"value\"}");
            sb.AppendLine("> EOF");
            sb.AppendLine("> ```");
            sb.AppendLine();
        }

        /// <summary>
        /// Override to inject additional markdown content immediately after
        /// <see cref="BuildToolCommand"/> and before <see cref="BuildToolCommandWithAuth"/>
        /// (or before the next section when <see cref="IncludeAuthorizationExample"/> is <c>false</c>).
        /// <para>
        /// Injected position in the generated document:
        /// <code>
        /// ```bash
        /// curl -X POST {host}/api/tools/{name} \
        ///   -H "Content-Type: application/json" \
        ///   -d '{...}'
        /// ```
        ///                          ← content appended HERE
        /// #### With Authorization (if required)   (when IncludeAuthorizationExample = true)
        /// </code>
        /// </para>
        /// </summary>
        /// <param name="sb">The <see cref="StringBuilder"/> that accumulates the skill file content.</param>
        protected virtual void BuildInputExampleNotes(StringBuilder sb)
        {
            // No notes by default; override to add custom notes below the input example.
        }

        /// <summary>
        /// Override to replace the "With Authorization" command block in the
        /// <c>## How to Call</c> section. By default emits a <c>curl</c> snippet with a
        /// <c>Bearer</c> token header. Only called when <see cref="IncludeAuthorizationExample"/> is <c>true</c>.
        /// Called after <see cref="BuildInputExampleNotes"/>, before <see cref="BuildInputAuthorizationNotes"/>.
        /// <para>
        /// Default output:
        /// <code>
        /// #### With Authorization (if required)
        ///
        /// ```bash
        /// curl -X POST {host}/api/tools/{name} \
        ///   -H "Content-Type: application/json" \
        ///   -H "Authorization: Bearer YOUR_TOKEN" \
        ///   -d '{inputExample}'
        /// ```
        /// </code>
        /// </para>
        /// </summary>
        /// <param name="sb">The <see cref="StringBuilder"/> that accumulates the skill file content.</param>
        /// <param name="tool">The tool being documented.</param>
        /// <param name="host">The server host URL (trailing slash already trimmed).</param>
        /// <param name="inputExample">Compact JSON example string produced by <see cref="BuildInputExample"/>.</param>
        protected virtual void BuildToolCommandWithAuth(StringBuilder sb, IRunTool tool, string host, string inputExample)
        {
            sb.AppendLine("#### With Authorization (if required)");
            sb.AppendLine();
            sb.AppendLine("```bash");
            sb.AppendLine($"curl -X POST {host}{GetApiRoutePrefix(tool)}/{tool.Name} \\");
            sb.AppendLine("  -H \"Content-Type: application/json\" \\");
            sb.AppendLine("  -H \"Authorization: Bearer YOUR_TOKEN\" \\");
            sb.AppendLine($"  -d '{inputExample}'");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        /// <summary>
        /// Override to inject additional markdown content immediately after
        /// <see cref="BuildToolCommandWithAuth"/> in the <c>## How to Call</c> section.
        /// This method is only called when <see cref="IncludeAuthorizationExample"/> is <c>true</c>.
        /// <para>
        /// Injected position in the generated document:
        /// <code>
        /// #### With Authorization (if required)
        /// ```bash
        /// curl -X POST {host}/api/tools/{name} \
        ///   -H "Authorization: Bearer YOUR_TOKEN" \
        ///   -d '{...}'
        /// ```
        ///                          ← content appended HERE
        /// ## Input
        /// </code>
        /// </para>
        /// </summary>
        /// <param name="sb">The <see cref="StringBuilder"/> that accumulates the skill file content.</param>
        protected virtual void BuildInputAuthorizationNotes(StringBuilder sb)
        {
            // No notes by default; override to add custom notes below the authorization example.
        }

        /// <summary>
        /// Override to inject additional markdown content immediately after the <c>## Input</c>
        /// heading and before the parameter table (or before the JSON schema when
        /// <see cref="IncludeParameterTable"/> is <see langword="false"/>).
        /// <para>
        /// Injected position in the generated document:
        /// <code>
        /// ## Input
        ///
        ///                          ← content appended HERE
        /// | Name | Type | Required | Description |
        /// |------|------|----------|-------------|
        /// </code>
        /// </para>
        /// </summary>
        /// <param name="sb">The <see cref="StringBuilder"/> that accumulates the skill file content.</param>
        protected virtual void BuildInputSectionNotes(StringBuilder sb)
        {
            // No notes by default; override to add content at the top of the Input section.
        }

        /// <summary>
        /// Override to inject additional markdown content immediately after the parameter table
        /// and before the <c>### Input JSON Schema</c> block (or before
        /// <see cref="SkillAdditionalContentPosition.AfterInput"/> additional content when
        /// <see cref="IncludeInputJsonSchema"/> is <see langword="false"/>).
        /// This method is only called when <see cref="IncludeParameterTable"/> is <see langword="true"/>.
        /// <para>
        /// Injected position in the generated document:
        /// <code>
        /// | Name | Type | Required | Description |
        /// | `param` | `string` | Yes | ... |
        ///
        ///                          ← content appended HERE
        /// ### Input JSON Schema
        /// </code>
        /// </para>
        /// </summary>
        /// <param name="sb">The <see cref="StringBuilder"/> that accumulates the skill file content.</param>
        protected virtual void BuildParameterTableNotes(StringBuilder sb)
        {
            // No notes by default; override to add content after the parameter table.
        }

        /// <summary>
        /// Override to replace the <c>### Input JSON Schema</c> code block.
        /// Called after <see cref="BuildParameterTableNotes"/>, before <see cref="BuildInputJsonSchemaNotes"/>.
        /// Only called when <see cref="IncludeInputJsonSchema"/> is <see langword="true"/>.
        /// <para>
        /// Default output:
        /// <code>
        /// ### Input JSON Schema
        ///
        /// ```json
        /// { ... }
        /// ```
        /// </code>
        /// </para>
        /// </summary>
        /// <param name="sb">The <see cref="StringBuilder"/> that accumulates the skill file content.</param>
        /// <param name="tool">The tool being documented.</param>
        protected virtual void BuildInputJsonSchemaBlock(StringBuilder sb, IRunTool tool)
        {
            var schema = tool.InputSchema;

            if (!IncludeInputSchemaPropertyDescriptions && schema != null)
                schema = StripPropertyDescriptions(schema);

            sb.AppendLine("### Input JSON Schema");
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.AppendLine(PrettyPrintJson(schema));
            sb.AppendLine("```");
            sb.AppendLine();
        }

        /// <summary>
        /// Override to inject additional markdown content immediately after
        /// <see cref="BuildInputJsonSchemaBlock"/> and before the
        /// <see cref="SkillAdditionalContentPosition.AfterInput"/> additional content.
        /// This method is only called when <see cref="IncludeInputJsonSchema"/> is <see langword="true"/>.
        /// <para>
        /// Injected position in the generated document:
        /// <code>
        /// ### Input JSON Schema
        ///
        /// ```json
        /// { ... }
        /// ```
        ///
        ///                          ← content appended HERE
        /// ## Output
        /// </code>
        /// </para>
        /// </summary>
        /// <param name="sb">The <see cref="StringBuilder"/> that accumulates the skill file content.</param>
        protected virtual void BuildInputJsonSchemaNotes(StringBuilder sb)
        {
            // No notes by default; override to add content after the input JSON schema.
        }

        /// <summary>
        /// Override to inject additional markdown content immediately after the <c>## Output</c>
        /// heading and before <see cref="BuildOutputSchemaBlock"/>.
        /// This method is only called when <see cref="IncludeOutputSection"/> is <see langword="true"/>.
        /// <para>
        /// Injected position in the generated document:
        /// <code>
        /// ## Output
        ///
        ///                          ← content appended HERE
        /// ### Output JSON Schema
        /// </code>
        /// </para>
        /// </summary>
        /// <param name="sb">The <see cref="StringBuilder"/> that accumulates the skill file content.</param>
        protected virtual void BuildOutputSectionNotes(StringBuilder sb)
        {
            // No notes by default; override to add content at the top of the Output section.
        }

        /// <summary>
        /// Override to replace the output schema block (or the "no structured output" text).
        /// Called after <see cref="BuildOutputSectionNotes"/>, before <see cref="BuildOutputSchemaNotes"/>.
        /// Only called when <see cref="IncludeOutputSection"/> is <see langword="true"/>.
        /// <para>
        /// Default output (when <c>tool.OutputSchema</c> is not null):
        /// <code>
        /// ### Output JSON Schema
        ///
        /// ```json
        /// { ... }
        /// ```
        /// </code>
        /// When <c>tool.OutputSchema</c> is null:
        /// <code>
        /// This tool does not return structured output.
        /// </code>
        /// </para>
        /// </summary>
        /// <param name="sb">The <see cref="StringBuilder"/> that accumulates the skill file content.</param>
        /// <param name="tool">The tool being documented.</param>
        protected virtual void BuildOutputSchemaBlock(StringBuilder sb, IRunTool tool)
        {
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
        }

        /// <summary>
        /// Override to inject additional markdown content immediately after
        /// <see cref="BuildOutputSchemaBlock"/> and before the end of the <c>## Output</c> section.
        /// This method is only called when <see cref="IncludeOutputSection"/> is <see langword="true"/>.
        /// <para>
        /// Injected position in the generated document:
        /// <code>
        /// ### Output JSON Schema
        ///
        /// ```json
        /// { ... }
        /// ```
        ///                          ← content appended HERE
        ///
        /// (end of document / AdditionalContent End)
        /// </code>
        /// </para>
        /// </summary>
        /// <param name="sb">The <see cref="StringBuilder"/> that accumulates the skill file content.</param>
        protected virtual void BuildOutputSchemaNotes(StringBuilder sb)
        {
            // No notes by default; override to add content after the output schema.
        }

        /// <summary>
        /// Appends the parameter table rows to <paramref name="sb"/> using <paramref name="inputSchema"/>.
        /// Override to change the table format or add extra columns.
        /// </summary>
        protected virtual void AppendParameterTable(StringBuilder sb, JsonNode? inputSchema)
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

        /// <summary>
        /// Builds a compact JSON example string from <paramref name="inputSchema"/>.
        /// Override to produce a different example format.
        /// </summary>
        protected virtual string BuildInputExample(JsonNode? inputSchema)
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

        /// <summary>
        /// Pretty-prints a <see cref="JsonNode"/> to an indented JSON string.
        /// Override to change serialisation options or handle errors differently.
        /// </summary>
        protected virtual string PrettyPrintJson(JsonNode? node)
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

        // ── Protected static utilities ───────────────────────────────────────

        /// <summary>
        /// Returns the HTTP API route prefix for a tool based on its <see cref="IRunTool.ToolType"/>.
        /// Standard tools use <c>/api/tools</c>; system tools use <c>/api/system-tools</c>.
        /// </summary>
        protected static string GetApiRoutePrefix(IRunTool tool)
        {
            return tool.ToolType == McpToolType.System ? "/api/system-tools" : "/api/tools";
        }

        /// <summary>
        /// Creates a placeholder JSON value for the given JSON Schema type string.
        /// Available to subclasses for use in custom <see cref="BuildInputExample"/> overrides.
        /// </summary>
        protected static JsonNode CreateExampleValue(string type, JsonObject? schema)
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

        /// <summary>
        /// Converts a tool name into a valid Agent Skills directory/name:
        /// lowercase alphanumeric and hyphens only, no leading/trailing/consecutive hyphens.
        /// Available to subclasses for use in custom <see cref="BuildNameMap"/> overrides.
        /// </summary>
        protected static string SanitizeSkillName(string name)
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
        /// Returns a stable 8-character lowercase hex string derived from <paramref name="value"/>
        /// using FNV-1a 32-bit over the UTF-8 byte representation, so the suffix is consistent
        /// across runs and runtimes and handles the full Unicode range correctly.
        /// All 32 bits are used to keep the collision probability negligible even with large tool sets.
        /// Available to subclasses for use in custom <see cref="BuildNameMap"/> overrides.
        /// </summary>
        protected static string StableShortHash(string value)
        {
            uint hash = 2166136261u;
            foreach (byte b in Encoding.UTF8.GetBytes(value))
            {
                hash ^= b;
                hash *= 16777619u;
            }
            return hash.ToString("x8");
        }

        /// <summary>
        /// Escapes a string for safe use as a YAML scalar value.
        /// Available to subclasses for use in custom <see cref="BuildMarkdown"/> overrides.
        /// </summary>
        protected static string EscapeYaml(string value)
        {
            if (value.Contains(':') || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\\\"")}\"";
            return value;
        }

        // ── Private helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Returns a deep copy of <paramref name="schema"/> with <c>description</c> fields
        /// removed from each entry inside the top-level <c>properties</c> object.
        /// The <c>$defs</c> section and all other nodes are left intact.
        /// </summary>
        private static JsonNode StripPropertyDescriptions(JsonNode schema)
        {
            // Deep-copy to avoid mutating the original schema
            var copy = JsonNode.Parse(schema.ToJsonString())!;

            if (copy["properties"] is JsonObject properties)
            {
                foreach (var kvp in properties)
                {
                    if (kvp.Value is JsonObject propObj)
                        propObj.Remove("description");
                }
            }

            return copy;
        }

        /// <summary>
        /// Appends <paramref name="content"/> to <paramref name="sb"/> only when
        /// <see cref="AdditionalContentPosition"/> matches <paramref name="targetPosition"/>
        /// and the content is non-empty.
        /// </summary>
        private void AppendAdditionalContent(StringBuilder sb, string? content, SkillAdditionalContentPosition targetPosition)
        {
            if (string.IsNullOrEmpty(content))
                return;
            if (AdditionalContentPosition == SkillAdditionalContentPosition.None)
                return;
            if (AdditionalContentPosition != targetPosition)
                return;

            sb.AppendLine(content);
            sb.AppendLine();
        }
    }
}
