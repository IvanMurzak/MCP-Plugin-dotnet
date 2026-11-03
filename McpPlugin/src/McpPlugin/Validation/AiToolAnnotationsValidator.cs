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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet.Utils;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>What an <see cref="AiToolAnnotationViolation"/> is about.</summary>
    public enum AiToolAnnotationViolationKind
    {
        /// <summary>The tool has no non-empty <c>title</c>.</summary>
        MissingTitle,

        /// <summary>The tool sets neither <c>readOnlyHint</c> nor <c>destructiveHint</c> explicitly.</summary>
        MissingSafetyHint,

        /// <summary>The tool declares both <c>readOnlyHint = true</c> and <c>destructiveHint = true</c>.</summary>
        ContradictoryHints
    }

    /// <summary>One tool that does not meet the annotation rules checked by <see cref="AiToolAnnotationsValidator"/>.</summary>
    public sealed class AiToolAnnotationViolation
    {
        /// <summary>The MCP tool name.</summary>
        public string ToolName { get; }

        /// <summary>The rule the tool breaks.</summary>
        public AiToolAnnotationViolationKind Kind { get; }

        /// <summary>The method that declares the tool, or <see langword="null"/> when validated from a <c>tools/list</c> entry.</summary>
        public MethodInfo? Method { get; }

        /// <summary>A human-readable description of the violation, naming the tool and (when known) its method.</summary>
        public string Message { get; }

        internal AiToolAnnotationViolation(string toolName, AiToolAnnotationViolationKind kind, MethodInfo? method = null)
        {
            ToolName = toolName ?? string.Empty;
            Kind = kind;
            Method = method;

            var where = method == null ? string.Empty : $" ({method.DeclaringType?.FullName}.{method.Name})";
            Message = kind switch
            {
                AiToolAnnotationViolationKind.MissingTitle => $"Tool '{ToolName}'{where} has no title. Set a non-empty Title.",
                AiToolAnnotationViolationKind.MissingSafetyHint => $"Tool '{ToolName}'{where} sets neither ReadOnlyHint nor DestructiveHint. Set at least one explicitly.",
                AiToolAnnotationViolationKind.ContradictoryHints => $"Tool '{ToolName}'{where} sets both ReadOnlyHint = true and DestructiveHint = true.",
                _ => $"Tool '{ToolName}'{where}: {kind}."
            };
        }

        public override string ToString() => Message;
    }

    /// <summary>
    /// Checks that tools carry the MCP annotations that tool directories (e.g. the Claude Connectors Directory)
    /// require: a non-empty <c>title</c> and an explicit <c>readOnlyHint</c> or <c>destructiveHint</c>. A hint
    /// left unset is omitted from <c>tools/list</c> (never defaulted by this library), so only the tool author
    /// can supply it. Call it from a unit test of the engine/plugin repo, for example:
    /// <code>
    /// AiToolAnnotationsValidator.Validate(typeof(MyTools).Assembly).ShouldBeEmpty();
    /// // or, test-framework neutral:
    /// AiToolAnnotationsValidator.ThrowIfAny(AiToolAnnotationsValidator.Validate(typeof(MyTools).Assembly));
    /// </code>
    /// </summary>
    public static class AiToolAnnotationsValidator
    {
        /// <summary>
        /// Validates every <see cref="AiToolAttribute"/> method of every <see cref="AiToolTypeAttribute"/> class in
        /// <paramref name="assemblies"/> (the same classes <c>WithToolsFromAssembly</c> registers; the builder's
        /// ignore rules are not applied). System tools (<see cref="McpToolType.System"/>) are skipped: they are
        /// never listed to MCP clients.
        /// </summary>
        public static IReadOnlyList<AiToolAnnotationViolation> Validate(params Assembly[] assemblies)
        {
            if (assemblies == null)
                throw new ArgumentNullException(nameof(assemblies));

            return ValidateTypes(assemblies
                .SelectMany(AssemblyUtils.GetAssemblyTypes)
                .Where(type => Attribute.IsDefined(type, typeof(AiToolTypeAttribute))));
        }

        /// <summary>
        /// Validates every <see cref="AiToolAttribute"/> method declared on <paramref name="types"/> (the same
        /// methods <c>WithTools(type)</c> registers, inherited ones included). System tools are skipped.
        /// </summary>
        public static IReadOnlyList<AiToolAnnotationViolation> ValidateTypes(IEnumerable<Type> types)
        {
            if (types == null)
                throw new ArgumentNullException(nameof(types));

            var violations = new List<AiToolAnnotationViolation>();
            foreach (var type in types)
            {
                foreach (var method in type.GetMethods(McpPluginBuilder.MethodBindingFlags))
                {
                    var attr = McpPluginBuilder.GetToolAttribute(method);
                    if (attr == null || attr.ToolType == McpToolType.System)
                        continue;

                    Collect(violations, attr.Name, attr.Title, attr.ReadOnlyHintValue, attr.DestructiveHintValue, method);
                }
            }
            return violations;
        }

        /// <summary>
        /// Validates tools as they appear in a <c>tools/list</c> catalogue (e.g. the result of
        /// <c>ToolManager.RunListTool</c>), which also covers tools registered at runtime through
        /// <c>AddTool</c> rather than by attribute.
        /// </summary>
        public static IReadOnlyList<AiToolAnnotationViolation> ValidateTools(IEnumerable<ResponseListTool> tools)
        {
            if (tools == null)
                throw new ArgumentNullException(nameof(tools));

            var violations = new List<AiToolAnnotationViolation>();
            foreach (var tool in tools)
                Collect(violations, tool.Name, tool.Title, tool.ReadOnlyHint, tool.DestructiveHint, method: null);
            return violations;
        }

        /// <summary>
        /// Throws an <see cref="InvalidOperationException"/> listing <paramref name="violations"/>; does nothing
        /// when the list is empty.
        /// </summary>
        public static void ThrowIfAny(IReadOnlyList<AiToolAnnotationViolation> violations)
        {
            if (violations == null)
                throw new ArgumentNullException(nameof(violations));
            if (violations.Count == 0)
                return;

            throw new InvalidOperationException(
                $"{violations.Count} tool annotation violation(s):\n- " +
                string.Join("\n- ", violations.Select(v => v.Message)));
        }

        static void Collect(List<AiToolAnnotationViolation> violations, string name, string? title, bool? readOnly, bool? destructive, MethodInfo? method)
        {
            if (string.IsNullOrWhiteSpace(title))
                violations.Add(new AiToolAnnotationViolation(name, AiToolAnnotationViolationKind.MissingTitle, method));

            if (readOnly == null && destructive == null)
                violations.Add(new AiToolAnnotationViolation(name, AiToolAnnotationViolationKind.MissingSafetyHint, method));
            else if (readOnly == true && destructive == true)
                violations.Add(new AiToolAnnotationViolation(name, AiToolAnnotationViolationKind.ContradictoryHints, method));
        }
    }
}
