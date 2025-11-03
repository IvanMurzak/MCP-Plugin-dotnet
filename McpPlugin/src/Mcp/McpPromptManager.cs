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
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.Logging;
using R3;

namespace com.IvanMurzak.McpPlugin
{
    public class McpPromptManager : IPromptManager
    {
        protected readonly ILogger _logger;
        protected readonly Reflector _reflector;
        readonly PromptRunnerCollection _prompts;
        readonly Subject<Unit> _onPromptsUpdated = new();
        readonly CancellationTokenSource _cancellationTokenSource = new();

        public Reflector Reflector => _reflector;
        public Observable<Unit> OnPromptsUpdated => _onPromptsUpdated;

        public McpPromptManager(ILogger<McpPromptManager> logger, Reflector reflector, PromptRunnerCollection prompts)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("Ctor");
            _reflector = reflector ?? throw new ArgumentNullException(nameof(reflector));
            _prompts = prompts ?? throw new ArgumentNullException(nameof(prompts));

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("Registered prompts [{0}]:", prompts.Count);
                foreach (var kvp in prompts)
                    _logger.LogTrace("Prompt: {0}", kvp.Key);
            }
        }

        #region Prompts
        public int EnabledPromptsCount => _prompts.Count(kvp => kvp.Value.Enabled);
        public int TotalPromptsCount => _prompts.Count;
        public bool HasPrompt(string name) => _prompts.ContainsKey(name);
        public bool AddPrompt(IRunPrompt runner)
        {
            if (runner == null)
                throw new ArgumentNullException(nameof(runner));

            if (HasPrompt(runner.Name))
            {
                _logger.LogWarning("Prompt with Name '{0}' already exists. Skipping addition.", runner.Name);
                return false;
            }

            _prompts[runner.Name] = runner;
            _onPromptsUpdated.OnNext(Unit.Default);
            return true;
        }
        public bool RemovePrompt(string name)
        {
            if (!HasPrompt(name))
            {
                _logger.LogWarning("Prompt with Name '{0}' not found. Cannot remove.", name);
                return false;
            }

            var removed = _prompts.Remove(name);
            if (removed)
                _onPromptsUpdated.OnNext(Unit.Default);

            return removed;
        }
        public bool IsPromptEnabled(string name)
        {
            if (!_prompts.TryGetValue(name, out var runner))
            {
                _logger.LogWarning("Prompt with Name '{0}' not found.", name);
                return false;
            }

            return runner.Enabled;
        }
        public bool SetPromptEnabled(string name, bool enabled)
        {
            if (!_prompts.TryGetValue(name, out var runner))
            {
                _logger.LogWarning("Prompt with Name '{0}' not found.", name);
                return false;
            }

            _logger.LogInformation("Setting Prompt '{0}' enabled state to {1}.", name, enabled);

            runner.Enabled = enabled;
            _onPromptsUpdated.OnNext(Unit.Default);

            return true;
        }

        public Task<ResponseData<ResponseGetPrompt>> RunGetPrompt(RequestGetPrompt request) => RunGetPrompt(request, _cancellationTokenSource.Token);
        public async Task<ResponseData<ResponseGetPrompt>> RunGetPrompt(RequestGetPrompt request, CancellationToken cancellationToken = default)
        {
            if (!_prompts.TryGetValue(request.Name, out var runner))
            {
                return ResponseData<ResponseGetPrompt>
                    .Error(request.RequestID, $"Prompt with Name '{request.Name}' not found.")
                    .Log(_logger);
            }

            var result = await runner.Run(request.RequestID, request.Arguments, cancellationToken);

            result.Log(_logger);

            return result.Pack(request.RequestID);
        }

        public Task<ResponseData<ResponseListPrompts>> RunListPrompts(RequestListPrompts request) => RunListPrompts(request, _cancellationTokenSource.Token);
        public Task<ResponseData<ResponseListPrompts>> RunListPrompts(RequestListPrompts request, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Listing prompts. [{Count}]", _prompts.Count);
                var result = new ResponseListPrompts()
                {
                    Prompts = _prompts.Values
                        .Select(p => new ResponsePrompt()
                        {
                            Name = p.Name,
                            Title = p.Title,
                            Description = p.Description,
                            Arguments = p.InputSchema.ToResponsePromptArguments()
                        })
                        .ToList()
                };
                _logger.LogDebug("{0} Prompts listed.", result.Prompts.Count);

                return result
                    .Log(_logger)
                    .Pack(request.RequestID)
                    .TaskFromResult();
            }
            catch (Exception ex)
            {
                // Handle or log the exception as needed
                return ResponseData<ResponseListPrompts>.Error(request.RequestID, $"Failed to list tools. Exception: {ex}")
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
            _prompts.Clear();
        }
    }
}
