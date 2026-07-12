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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace com.IvanMurzak.McpPlugin.Server.Tools
{
    /// <summary>
    /// The server-native MCP tools of the account+instance pairing plane (mcp-authorize b4, design 04
    /// "Built-in selection tools"): <c>list_engine_instances</c>, <c>select_engine_instance</c>,
    /// <c>enroll_engine_plugin</c>. These are served by the RS itself (NOT proxied to a plugin) and are
    /// merged into <c>tools/list</c> alongside the paired plugin's tools by <see cref="ToolRouter"/>.
    /// Registered only in <c>oauth</c> mode (there is no account/instance concept in none/required).
    /// </summary>
    public sealed class ServerNativeTools
    {
        public const string ListInstances = "list_engine_instances";
        public const string SelectInstance = "select_engine_instance";
        public const string EnrollPlugin = "enroll_engine_plugin";

        static readonly IReadOnlyList<Tool> _descriptors = BuildDescriptors();

        readonly AccountInstances _instances;
        readonly ISessionSelectionStore _selections;
        readonly IEnrollmentClient _enrollment;
        readonly ILogger? _logger;

        public ServerNativeTools(AccountInstances instances, ISessionSelectionStore selections, IEnrollmentClient enrollment, ILogger<ServerNativeTools>? logger = null)
        {
            _instances = instances ?? throw new ArgumentNullException(nameof(instances));
            _selections = selections ?? throw new ArgumentNullException(nameof(selections));
            _enrollment = enrollment ?? throw new ArgumentNullException(nameof(enrollment));
            _logger = logger;
        }

        /// <summary>The MCP tool descriptors merged into <c>tools/list</c> (design 04).</summary>
        public IReadOnlyList<Tool> Descriptors => _descriptors;

        /// <summary>True when <paramref name="name"/> is one of the server-native tool names.</summary>
        public static bool IsServerNativeTool(string? name)
            => name == ListInstances || name == SelectInstance || name == EnrollPlugin;

        /// <summary>Dispatches a server-native tool call. The caller guarantees <see cref="IsServerNativeTool"/>.</summary>
        public Task<ResponseCallTool> HandleAsync(string name, IReadOnlyDictionary<string, JsonElement> arguments, SelectionToolContext context, CancellationToken cancellationToken = default)
        {
            arguments ??= new Dictionary<string, JsonElement>();
            switch (name)
            {
                case ListInstances:
                    return Task.FromResult(HandleList(context));
                case SelectInstance:
                    return Task.FromResult(HandleSelect(arguments, context));
                case EnrollPlugin:
                    return HandleEnrollAsync(arguments, context, cancellationToken);
                default:
                    return Task.FromResult(ResponseCallTool.Error($"'{name}' is not a server-native tool."));
            }
        }

        // ─────────────────────────────── list_engine_instances ───────────────────────────────
        ResponseCallTool HandleList(SelectionToolContext context)
        {
            var instances = _instances.GetInstances(context.AccountId);
            if (instances.Count == 0)
                return ResponseCallTool.Success("No engine instances are connected for your account. Use enroll_engine_plugin to set one up.");

            var selected = _selections.Get(context.SessionId);
            var lines = instances
                .OrderByDescending(i => i.LastActiveAt)
                .Select(i =>
                {
                    var mark = string.Equals(i.InstanceId, selected, StringComparison.Ordinal) ? " (selected)" : string.Empty;
                    return $"- {i.InstanceId}: {i.Engine}:{i.ProjectName} on {i.MachineName}; " +
                           $"connected {i.ConnectedAt:u}, last active {i.LastActiveAt:u}{mark}";
                });

            return ResponseCallTool.Success("Connected engine instances for your account:\n" + string.Join("\n", lines));
        }

        // ─────────────────────────────── select_engine_instance ──────────────────────────────
        ResponseCallTool HandleSelect(IReadOnlyDictionary<string, JsonElement> arguments, SelectionToolContext context)
        {
            if (string.IsNullOrEmpty(context.SessionId))
                return ResponseCallTool.Error("Cannot set a selection without an active MCP session.");

            var instances = _instances.GetInstances(context.AccountId);
            if (instances.Count == 0)
                return ResponseCallTool.Error("No engine instances are connected for your account. Use enroll_engine_plugin to set one up.");

            var instanceId = GetStringArg(arguments, "instance_id");
            var projectName = GetStringArg(arguments, "project_name");

            PluginInstance? target;
            if (!string.IsNullOrEmpty(instanceId))
            {
                target = instances.FirstOrDefault(i => string.Equals(i.InstanceId, instanceId, StringComparison.Ordinal));
                if (target == null)
                    return ResponseCallTool.Error($"No connected instance has id '{instanceId}'. Use list_engine_instances to see valid ids.");
            }
            else if (!string.IsNullOrEmpty(projectName))
            {
                var matches = instances.Where(i => string.Equals(i.ProjectName, projectName, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 0)
                    return ResponseCallTool.Error($"No connected instance is for project '{projectName}'. Use list_engine_instances to see connected projects.");
                if (matches.Count > 1)
                    return ResponseCallTool.Error($"Multiple instances match project '{projectName}'; pass instance_id instead (see list_engine_instances).");
                target = matches[0];
            }
            else
            {
                return ResponseCallTool.Error("Provide 'instance_id' (preferred) or a unique 'project_name'.");
            }

            // A sticky selection may narrow a pin but NEVER override it to a different project (design 04 step 2).
            if (!string.IsNullOrEmpty(context.ProjectPin) && !target.MatchesPin(context.ProjectPin))
                return ResponseCallTool.Error("This session is pinned to a project; you can only select an instance of the pinned project.");

            _selections.Set(context.SessionId!, target.InstanceId);
            // Take effect within this request too (routing reads the ambient AsyncLocal).
            McpSessionTokenContext.CurrentSelectedInstanceId = target.InstanceId;

            return ResponseCallTool.Success($"Selected {target.Engine}:{target.ProjectName} ({target.InstanceId}) for this session.");
        }

        // ─────────────────────────────── enroll_engine_plugin ────────────────────────────────
        async Task<ResponseCallTool> HandleEnrollAsync(IReadOnlyDictionary<string, JsonElement> arguments, SelectionToolContext context, CancellationToken cancellationToken)
        {
            var engine = (GetStringArg(arguments, "engine") ?? string.Empty).Trim().ToLowerInvariant();
            if (engine != "unity" && engine != "godot" && engine != "unreal")
                return ResponseCallTool.Error("Argument 'engine' is required and must be one of: unity, godot, unreal.");

            if (string.IsNullOrEmpty(context.Bearer))
                return ResponseCallTool.Error("No session credential is available to enroll with. Sign in first.");

            var result = await _enrollment.CreateAsync(engine, context.Bearer!, cancellationToken);
            if (!result.Success || string.IsNullOrEmpty(result.EnrollCode))
                return ResponseCallTool.Error(result.Error ?? "Enrollment failed.");

            var command = $"npx {CliPackage(engine)} install-plugin --enroll {result.EnrollCode}";
            return ResponseCallTool.Success(
                $"Run this in your {engine} project folder, then open the project in the editor:\n\n{command}");
        }

        /// <summary>Maps an engine to its published CLI npm package (design 04 step 5 / workspace CLIs).</summary>
        public static string CliPackage(string engine) => engine switch
        {
            "unity" => "unity-mcp-cli",
            "godot" => "godot-cli",
            "unreal" => "unreal-mcp-cli",
            _ => $"{engine}-mcp-cli"
        };

        static string? GetStringArg(IReadOnlyDictionary<string, JsonElement> arguments, string key)
        {
            if (arguments != null && arguments.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String)
            {
                var value = el.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            return null;
        }

        static IReadOnlyList<Tool> BuildDescriptors()
        {
            return new List<Tool>
            {
                new Tool
                {
                    Name = ListInstances,
                    Description = "List the live game-engine instances connected for your account (engine, project, machine, connected/last-active), and which one this session has selected.",
                    InputSchema = Schema("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"),
                    Annotations = new ToolAnnotations { Title = "List engine instances", ReadOnlyHint = true, OpenWorldHint = false }
                },
                new Tool
                {
                    Name = SelectInstance,
                    Description = "Set the sticky engine instance for this MCP session. Provide instance_id (preferred) or a unique project_name. May narrow a project pin but never overrides it to another project.",
                    InputSchema = Schema("{\"type\":\"object\",\"properties\":{\"instance_id\":{\"type\":\"string\",\"description\":\"The instance id from list_engine_instances.\"},\"project_name\":{\"type\":\"string\",\"description\":\"A unique project name (alternative to instance_id).\"}},\"additionalProperties\":false}"),
                    Annotations = new ToolAnnotations { Title = "Select engine instance", ReadOnlyHint = false, OpenWorldHint = false }
                },
                new Tool
                {
                    Name = EnrollPlugin,
                    Description = "Start connecting a new game engine to your account. Returns a ready-to-run 'npx <engine>-cli install-plugin --enroll <code>' command to run in the project folder.",
                    InputSchema = Schema("{\"type\":\"object\",\"properties\":{\"engine\":{\"type\":\"string\",\"enum\":[\"unity\",\"godot\",\"unreal\"],\"description\":\"The engine to enroll.\"}},\"required\":[\"engine\"],\"additionalProperties\":false}"),
                    Annotations = new ToolAnnotations { Title = "Enroll engine plugin", ReadOnlyHint = false, OpenWorldHint = true }
                }
            };
        }

        static JsonElement Schema(string json) => JsonSerializer.Deserialize<JsonElement>(json);
    }
}
