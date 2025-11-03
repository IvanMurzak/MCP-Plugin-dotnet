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
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet;
using com.IvanMurzak.ReflectorNet.Utils;
using Microsoft.Extensions.Logging;
using R3;

namespace com.IvanMurzak.McpPlugin
{
    public class McpToolManager : IToolManager
    {
        static readonly JsonElement EmptyInputSchema = JsonDocument.Parse("{\"type\":\"object\"}").RootElement;

        protected readonly ILogger _logger;
        protected readonly Reflector _reflector;
        readonly ToolRunnerCollection _tools;
        readonly Subject<Unit> _onToolsUpdated = new();
        readonly CancellationTokenSource _cancellationTokenSource = new();

        public Reflector Reflector => _reflector;
        public Observable<Unit> OnToolsUpdated => _onToolsUpdated;

        public McpToolManager(ILogger<McpToolManager> logger, Reflector reflector, ToolRunnerCollection tools)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("Ctor");
            _reflector = reflector ?? throw new ArgumentNullException(nameof(reflector));
            _tools = tools ?? throw new ArgumentNullException(nameof(tools));

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("Registered tools [{0}]:", tools.Count);
                foreach (var kvp in tools)
                    _logger.LogTrace("Tool: {0}", kvp.Key);
            }
        }

        #region Tools
        public int EnabledToolsCount => _tools.Count(kvp => kvp.Value.Enabled);
        public int TotalToolsCount => _tools.Count;
        public bool HasTool(string name) => _tools.ContainsKey(name);
        public bool AddTool(string name, IRunTool runner)
        {
            if (HasTool(name))
            {
                _logger.LogWarning("Tool with Name '{0}' already exists. Skipping addition.", name);
                return false;
            }

            _tools[name] = runner;
            _onToolsUpdated.OnNext(Unit.Default);
            return true;
        }
        public bool RemoveTool(string name)
        {
            if (!HasTool(name))
            {
                _logger.LogWarning("Tool with Name '{0}' not found. Cannot remove.", name);
                return false;
            }

            var removed = _tools.Remove(name);
            if (removed)
                _onToolsUpdated.OnNext(Unit.Default);

            return removed;
        }
        public bool IsToolEnabled(string name)
        {
            if (!_tools.TryGetValue(name, out var runner))
            {
                _logger.LogWarning("Tool with Name '{0}' not found.", name);
                return false;
            }

            return runner.Enabled;
        }
        public bool SetToolEnabled(string name, bool enabled)
        {
            if (!_tools.TryGetValue(name, out var runner))
            {
                _logger.LogWarning("Tool with Name '{0}' not found.", name);
                return false;
            }

            runner.Enabled = enabled;
            _onToolsUpdated.OnNext(Unit.Default);

            return true;
        }

        public Task<ResponseData<ResponseCallTool>> RunCallTool(RequestCallTool data) => RunCallTool(data, _cancellationTokenSource.Token);
        public async Task<ResponseData<ResponseCallTool>> RunCallTool(RequestCallTool data, CancellationToken cancellationToken = default)
        {
            if (data == null)
                return ResponseData<ResponseCallTool>.Error(Common.Consts.Guid.Zero, "Tool data is null.")
                    .Log(_logger);

            if (string.IsNullOrEmpty(data.Name))
                return ResponseData<ResponseCallTool>.Error(data.RequestID, "Tool.Name is null.")
                    .Log(_logger);

            if (!_tools.TryGetValue(data.Name, out var runner))
                return ResponseData<ResponseCallTool>.Error(data.RequestID, $"Tool with Name '{data.Name}' not found.")
                    .Log(_logger);
            try
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    var message = data.Arguments == null
                        ? $"Run tool '{data.Name}' with no parameters."
                        : $"Run tool '{data.Name}' with parameters[{data.Arguments.Count}]:\n{string.Join(",\n", data.Arguments)}\n";
                    _logger.LogInformation(message);
                }

                var result = await runner.Run(data.RequestID, data.Arguments, cancellationToken);
                if (result == null)
                    return ResponseData<ResponseCallTool>.Error(data.RequestID, $"Tool '{data.Name}' returned null result.")
                        .Log(_logger);

                result.Log(_logger);

                return result.Pack(data.RequestID);
            }
            catch (Exception ex)
            {
                // Handle or log the exception as needed
                return ResponseData<ResponseCallTool>.Error(data.RequestID, $"Failed to run tool '{data.Name}'. Exception: {ex}")
                    .Log(_logger, $"RunCallTool[{data.Name}]", ex);
            }
        }

        public Task<ResponseData<ResponseListTool[]>> RunListTool(RequestListTool data) => RunListTool(data, _cancellationTokenSource.Token);
        public Task<ResponseData<ResponseListTool[]>> RunListTool(RequestListTool data, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Listing tools.");
                var result = _tools
                    .Select(kvp =>
                    {
                        var response = new ResponseListTool()
                        {
                            Name = kvp.Key,
                            Title = kvp.Value.Title,
                            Description = kvp.Value.Description,
                            InputSchema = kvp.Value.InputSchema.ToJsonElement() ?? EmptyInputSchema
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
                _logger.LogDebug("{0} Tools listed.", result.Length);

                return result
                    .Log(_logger)
                    .Pack(data.RequestID)
                    .TaskFromResult();
            }
            catch (Exception ex)
            {
                // Handle or log the exception as needed
                return ResponseData<ResponseListTool[]>.Error(data.RequestID, $"Failed to list tools. Exception: {ex}")
                    .Log(_logger, "RunListTool", ex)
                    .TaskFromResult();
            }
        }
        #endregion

        public void ForceDisconnect()
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            if (!_cancellationTokenSource.IsCancellationRequested)
                _cancellationTokenSource.Cancel();

            _cancellationTokenSource.Dispose();
            _tools.Clear();
        }
    }
}
