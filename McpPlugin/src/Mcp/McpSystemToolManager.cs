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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.ReflectorNet;
using com.IvanMurzak.ReflectorNet.Utils;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// Manages system tools — internal tools available via HTTP API but NOT exposed to MCP clients.
    /// System tools are discovered via <see cref="McpPluginToolAttribute"/> with
    /// <see cref="McpPluginToolAttribute.ToolType"/> set to <see cref="McpToolType.System"/>.
    /// </summary>
    public class McpSystemToolManager : ISystemToolManager
    {
        readonly ILogger _logger;
        readonly SystemToolRunnerCollection _tools;

        public McpSystemToolManager(ILogger<McpSystemToolManager> logger, SystemToolRunnerCollection tools)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("Ctor");
            _tools = tools ?? throw new ArgumentNullException(nameof(tools));

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("Registered system tools [{0}]:", tools.Count);
                foreach (var kvp in tools)
                    _logger.LogTrace("System tool: {0}", kvp.Key);
            }
        }

        public int TotalToolsCount => _tools.Count;

        public IEnumerable<IRunTool> GetAllTools() => _tools.Values.ToList();

        public bool HasTool(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            return _tools.ContainsKey(name);
        }

        public async Task<ResponseData<ResponseCallTool>> RunSystemTool(RequestCallTool request, CancellationToken cancellationToken = default)
        {
            if (request == null)
                return ResponseData<ResponseCallTool>.Error(string.Empty, "Request is null.");

            var name = request.Name;
            if (string.IsNullOrWhiteSpace(name))
                return ResponseData<ResponseCallTool>.Error(request.RequestID, "System tool name is empty.");

            if (!_tools.TryGetValue(name, out var tool))
            {
                _logger.LogWarning("System tool '{name}' not found. Available: [{available}]",
                    name, string.Join(", ", _tools.Keys.OrderBy(k => k)));
                return ResponseData<ResponseCallTool>.Error(request.RequestID, $"System tool '{name}' not found.");
            }

            try
            {
                _logger.LogDebug("Executing system tool '{name}'.", name);
                var result = await tool.Run(request.RequestID, request.Arguments, cancellationToken);
                return result.Pack(request.RequestID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "System tool '{name}' failed.", name);
                return ResponseData<ResponseCallTool>.Error(request.RequestID, $"System tool '{name}' failed: {ex.Message}");
            }
        }

        public Task<ResponseData<ResponseListTool[]>> RunListSystemTool(RequestListTool request, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Listing system tools.");
                var result = _tools
                    .Select(kvp =>
                    {
                        var response = new ResponseListTool()
                        {
                            Name = kvp.Value.Name,
                            Enabled = kvp.Value.Enabled,
                            Title = kvp.Value.Title,
                            Description = kvp.Value.Description,
                            InputSchema = kvp.Value.InputSchema.ToJsonElement() ?? Common.Consts.MCP.EmptyInputSchema,
                            ReadOnlyHint = kvp.Value.ReadOnlyHint,
                            DestructiveHint = kvp.Value.DestructiveHint,
                            IdempotentHint = kvp.Value.IdempotentHint,
                            OpenWorldHint = kvp.Value.OpenWorldHint
                        };
                        if (kvp.Value.OutputSchema == null)
                            return response;

                        if (kvp.Value.OutputSchema is not JsonNode jn)
                            return response;

                        if (jn.GetValueKind() != JsonValueKind.Object)
                            return response;

                        if (jn[JsonSchema.Type]?.GetValue<string>() != JsonSchema.Object)
                            return response;

                        response.OutputSchema = jn.ToJsonElement();
                        return response;
                    })
                    .ToArray();
                _logger.LogDebug("{0} System tools listed.", result.Length);

                return result
                    .Log(_logger)
                    .Pack(request.RequestID)
                    .TaskFromResult();
            }
            catch (Exception ex)
            {
                return ResponseData<ResponseListTool[]>.Error(request.RequestID, $"Failed to list system tools. Exception: {ex}")
                    .Log(_logger, "RunListSystemTool", ex)
                    .TaskFromResult();
            }
        }
    }
}
