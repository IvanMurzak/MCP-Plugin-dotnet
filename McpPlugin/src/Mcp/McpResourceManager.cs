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
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.Logging;
using R3;

namespace com.IvanMurzak.McpPlugin
{
    public class McpResourceManager : IResourceManager
    {
        protected readonly ILogger _logger;
        protected readonly Reflector _reflector;
        protected readonly CompositeDisposable _disposables = new();
        readonly ResourceRunnerCollection _resources;
        readonly Subject<Unit> _onResourcesUpdated = new();

        public Reflector Reflector => _reflector;
        public Observable<Unit> OnResourcesUpdated => _onResourcesUpdated;

        public IEnumerable<IRunResource> GetAllResources() => _resources.Values.ToList();

        public McpResourceManager(ILogger<McpResourceManager> logger, Reflector reflector, ResourceRunnerCollection resources)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("Ctor");
            _reflector = reflector ?? throw new ArgumentNullException(nameof(reflector));
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("Registered resources [{0}]:", resources.Count);
                foreach (var kvp in resources)
                    _logger.LogTrace("Resource: {Name}. Route: {Route}", kvp.Key, kvp.Value.Route);
            }
        }

        #region Resources
        public int EnabledResourcesCount => _resources.Count(kvp => kvp.Value.Enabled);
        public int TotalResourcesCount => _resources.Count;
        public bool HasResource(string name) => _resources.ContainsKey(name);
        public bool AddResource(IRunResource resourceParams)
        {
            if (resourceParams == null)
                throw new ArgumentNullException(nameof(resourceParams));

            if (HasResource(resourceParams.Name))
            {
                _logger.LogWarning("Resource with Name '{0}' already exists. Skipping addition.", resourceParams.Name);
                return false;
            }
            _resources[resourceParams.Name] = resourceParams;
            _onResourcesUpdated.OnNext(Unit.Default);

            return true;
        }
        public bool RemoveResource(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Resource name is null or empty.", nameof(name));

            if (!HasResource(name))
            {
                _logger.LogWarning("Resource with Name '{0}' does not exist. Skipping removal.", name);
                return false;
            }

            _resources.Remove(name);
            _onResourcesUpdated.OnNext(Unit.Default);

            return true;
        }
        public bool IsResourceEnabled(string name)
        {
            if (!_resources.TryGetValue(name, out var runner))
            {
                _logger.LogWarning("Resource with Name '{0}' not found.", name);
                return false;
            }

            return runner.Enabled;
        }
        public bool SetResourceEnabled(string name, bool enabled)
        {
            if (!_resources.TryGetValue(name, out var runner))
            {
                _logger.LogWarning("Resource with Name '{0}' not found.", name);
                return false;
            }

            runner.Enabled = enabled;
            _onResourcesUpdated.OnNext(Unit.Default);
            return true;
        }
        public Task<ResponseData<ResponseResourceContent[]>> RunResourceContent(RequestResourceContent data) => RunResourceContent(data, default);
        public async Task<ResponseData<ResponseResourceContent[]>> RunResourceContent(RequestResourceContent data, CancellationToken cancellationToken = default)
        {
            if (data == null)
                throw new ArgumentException("Resource data is null.");

            if (data.Uri == null)
                throw new ArgumentException("Resource.Uri is null.");

            var runner = FindResourceContentRunner(data.Uri, _resources, out var uriTemplate)?.RunGetContent;
            if (runner == null || uriTemplate == null)
                throw new ArgumentException($"No route matches the URI: {data.Uri}");

            _logger.LogInformation("Executing resource '{0}'.", data.Uri);

            var parameters = ParseUriParameters(uriTemplate!, data.Uri);
            PrintParameters(parameters);

            // Execute the resource with the parameters from Uri
            var result = await runner.Run(parameters);
            return result.Pack(data.RequestID);
        }
        public Task<ResponseData<ResponseListResource[]>> RunListResources(RequestListResources data) => RunListResources(data, default);
        public async Task<ResponseData<ResponseListResource[]>> RunListResources(RequestListResources data, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Listing resources. [{Count}]", _resources.Count);
            var tasks = _resources.Values
                .Select(resource => resource.RunListContext.Run());

            await Task.WhenAll(tasks);

            return tasks
                .SelectMany(x => x.Result)
                .ToArray()
                .Pack(data.RequestID);
        }
        public Task<ResponseData<ResponseResourceTemplate[]>> RunResourceTemplates(RequestListResourceTemplates data) => RunResourceTemplates(data, default);
        public Task<ResponseData<ResponseResourceTemplate[]>> RunResourceTemplates(RequestListResourceTemplates data, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Listing resource templates. [{Count}]", _resources.Count);
            return _resources.Values
                .Select(resource => new ResponseResourceTemplate(resource.Route, resource.Name, resource.Description, resource.MimeType))
                .ToArray()
                .Pack(data.RequestID)
                .TaskFromResult();
        }
        IRunResource? FindResourceContentRunner(string uri, IDictionary<string, IRunResource> resources, out string? uriTemplate)
        {
            foreach (var route in resources)
            {
                if (IsMatch(route.Value.Route, uri))
                {
                    uriTemplate = route.Value.Route;
                    return route.Value;
                }
            }
            uriTemplate = null;
            return null;
        }
        #endregion

        #region Utils
        bool IsMatch(string uriTemplate, string uri)
        {
            // Convert pattern to regex
            var regexPattern = "^" + Regex.Replace(uriTemplate, @"\{(\w+)\}", @"(?<$1>[^/]+)") + "(?:/.*)?$";

            return Regex.IsMatch(uri, regexPattern);
        }

        IDictionary<string, object?> ParseUriParameters(string pattern, string uri)
        {
            var parameters = new Dictionary<string, object?>()
            {
                { "uri", uri }
            };

            // Convert pattern to regex
            var regexPattern = "^" + Regex.Replace(pattern, @"\{(\w+)\}", @"(?<$1>.+)") + "(?:/.*)?$";

            var regex = new Regex(regexPattern);
            var match = regex.Match(uri);

            if (match.Success)
            {
                foreach (var groupName in regex.GetGroupNames())
                {
                    if (groupName != "0") // Skip the entire match group
                    {
                        parameters[groupName] = match.Groups[groupName].Value;
                    }
                }
            }

            return parameters;
        }

        void PrintParameters(IDictionary<string, object?> parameters)
        {
            if (!_logger.IsEnabled(LogLevel.Debug))
                return;

            var parameterLogs = string.Join(Environment.NewLine, parameters.Select(kvp => $"{kvp.Key} = {kvp.Value ?? "null"}"));
            _logger.LogDebug("Parsed Parameters [{0}]:\n{1}", parameters.Count, parameterLogs);
        }
        #endregion

        public void Dispose()
        {
            _disposables.Dispose();
            _resources.Clear();
        }
    }
}
