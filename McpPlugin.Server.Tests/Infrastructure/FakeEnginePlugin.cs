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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure
{
    /// <summary>
    /// A minimal engine plugin over a REAL SignalR connection to a test host: the version handshake, a tool
    /// list and scripted tool calls. What an editor or a game build sends is set by <c>environment</c> and the
    /// optional declared role (hub query <c>client_role</c>, or the <c>X-Client-Role</c> header).
    /// </summary>
    public sealed class FakeEnginePlugin : IAsyncDisposable
    {
        public const string RqlSchema =
            "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"},\"max_targets\":{\"type\":\"integer\"},\"dry_run\":{\"type\":[\"boolean\",\"null\"]}},\"required\":[\"query\"]}";

        readonly string _baseUrl;
        readonly string _environment;
        readonly string? _declaredRole;
        readonly bool _roleInHeader;
        readonly string? _token;
        readonly Func<FakeEnginePlugin, RequestCallTool, Task<ResponseData<ResponseCallTool>>>? _onCall;
        readonly string _apiVersion;
        readonly CancellationTokenSource _disposed = new CancellationTokenSource();
        HubConnection? _connection;

        public FakeEnginePlugin(string baseUrl, string name, string environment, string? declaredRole = null,
            Func<FakeEnginePlugin, RequestCallTool, Task<ResponseData<ResponseCallTool>>>? onCall = null,
            string? apiVersion = null, bool roleInHeader = false, string? token = null)
        {
            _baseUrl = baseUrl;
            Name = name;
            _environment = environment;
            _declaredRole = declaredRole;
            _roleInHeader = roleInHeader;
            _token = token;
            _onCall = onCall;
            _apiVersion = apiVersion ?? Consts.ApiVersion;
        }

        public string Name { get; }

        /// <summary>Cancelled when the plugin is disposed: a handler that "hangs" waits on it, so it never outlives the test.</summary>
        public CancellationToken Disposed => _disposed.Token;
        public ConcurrentQueue<RequestCallTool> Calls { get; } = new ConcurrentQueue<RequestCallTool>();
        public TaskCompletionSource<string?> ForceDisconnected { get; } = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? ConnectionId => _connection?.ConnectionId;

        public ResponseListTool[] Tools { get; set; } = new[]
        {
            new ResponseListTool { Name = "echo", InputSchema = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}").RootElement.Clone() },
            new ResponseListTool { Name = "rql-query", InputSchema = JsonDocument.Parse(RqlSchema).RootElement.Clone() },
        };

        public async Task StartAsync()
        {
            var query = new List<string>();
            if (_declaredRole != null && !_roleInHeader)
                query.Add(Consts.MCP.Server.HubQuery.ClientRole + "=" + Uri.EscapeDataString(_declaredRole));
            if (_token != null)
                query.Add("access_token=" + Uri.EscapeDataString(_token));
            var url = _baseUrl + Consts.Hub.RemoteApp + (query.Count > 0 ? "?" + string.Join("&", query) : string.Empty);

            _connection = new HubConnectionBuilder()
                .WithUrl(url, options =>
                {
                    if (_declaredRole != null && _roleInHeader)
                        options.Headers[Consts.MCP.Server.ClientRoleHeader] = _declaredRole;
                })
                .AddJsonProtocol(options => SignalR_JsonConfiguration.ConfigureJsonSerializer(new Reflector(), options))
                .Build();

            _connection.On<RequestCallTool, ResponseData<ResponseCallTool>>("RunCallTool", async request =>
            {
                Calls.Enqueue(request);
                if (_onCall != null)
                    return await _onCall(this, request);
                return Success(request, $"{Name}:{request.Name}");
            });
            _connection.On<RequestListTool, ResponseData<ResponseListTool[]>>("RunListTool",
                request => ResponseData<ResponseListTool[]>.Success(request.RequestID).SetData(Tools));
            _connection.On<string?>("ForceDisconnect", reason => ForceDisconnected.TrySetResult(reason));
            _connection.On<McpClientData[]>("OnInitialClientData", _ => { });

            await _connection.StartAsync();
            var handshake = await _connection.InvokeAsync<VersionHandshakeResponse>("PerformVersionHandshake", new RequestVersionHandshake
            {
                RequestID = Guid.NewGuid().ToString(),
                ApiVersion = _apiVersion,
                PluginVersion = "0.0.0-test",
                Environment = _environment,
            });
            if (!handshake.Compatible)
                throw new InvalidOperationException("Handshake refused: " + handshake.Message);
        }

        /// <summary>Drops the connection the way a plugin does before a reload.</summary>
        public Task StopAsync() => _connection?.StopAsync() ?? Task.CompletedTask;

        /// <summary>True when the server told this plugin it was replaced within <paramref name="window"/>.</summary>
        public async Task<bool> WasEvictedWithin(TimeSpan window)
            => await Task.WhenAny(ForceDisconnected.Task, Task.Delay(window)) == ForceDisconnected.Task;

        public static ResponseData<ResponseCallTool> Success(RequestCallTool request, string text)
            => ResponseCallTool.Success(text).SetRequestID(request.RequestID).Pack(request.RequestID);

        public static ResponseData<ResponseCallTool> Error(RequestCallTool request, string text, ResponseErrorKind kind, int? status = null)
            => ResponseCallTool.Error(text, kind, status).SetRequestID(request.RequestID).Pack(request.RequestID);

        public async ValueTask DisposeAsync()
        {
            _disposed.Cancel();
            if (_connection != null)
                await _connection.DisposeAsync();
            _disposed.Dispose();
        }
    }

    /// <summary>MCP <c>tools/call</c> and REST tool calls against a <see cref="NoneAuthMcpHost"/>.</summary>
    public sealed class ToolCallClient : IDisposable
    {
        readonly NoneAuthMcpHost _host;
        readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        string? _session;
        int _nextId = 10;

        public ToolCallClient(NoneAuthMcpHost host) => _host = host;

        public FakeEnginePlugin Plugin(string name, string environment, string? declaredRole = null,
            Func<FakeEnginePlugin, RequestCallTool, Task<ResponseData<ResponseCallTool>>>? onCall = null,
            string? apiVersion = null, bool roleInHeader = false, string? token = null)
            => new FakeEnginePlugin(_host.BaseUrl, name, environment, declaredRole, onCall, apiVersion, roleInHeader, token);

        /// <summary>A <c>tools/call</c> over MCP: whether the result is an error, and its text.</summary>
        public async Task<(bool IsError, string Text)> CallAsync(string name, object arguments)
        {
            var id = Interlocked.Increment(ref _nextId);
            var response = await RawAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method = "tools/call", @params = new { name, arguments } }));
            var result = response["result"] ?? throw new Xunit.Sdk.XunitException("No result: " + response.ToJsonString());
            var text = string.Join("\n", (result["content"]?.AsArray() ?? new JsonArray())
                .Select(c => c?["text"]?.GetValue<string>()).Where(t => t != null));
            return (result["isError"]?.GetValue<bool>() == true, text);
        }

        /// <summary>Sends a JSON-RPC body written by the caller (so it can hold invalid escapes) and returns the response.</summary>
        public async Task<JsonNode> RawAsync(string body)
        {
            _session ??= await _host.HandshakeAsync(_http);
            using var request = new HttpRequestMessage(HttpMethod.Post, _host.BaseUrl + "/mcp")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("text/event-stream");
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _session);
            using var response = await _http.SendAsync(request);
            var text = NoneAuthMcpHost.Unwrap(await response.Content.ReadAsStringAsync());
            return JsonNode.Parse(text) ?? throw new Xunit.Sdk.XunitException($"Empty response ({(int)response.StatusCode}).");
        }

        /// <summary>POST <c>/api/tools/{name}</c>: the HTTP status and the JSON body.</summary>
        public async Task<(int Status, JsonNode? Body)> RestAsync(string name, string jsonBody)
        {
            using var response = await _http.PostAsync(_host.BaseUrl + "/api/tools/" + name, new StringContent(jsonBody, Encoding.UTF8, "application/json"));
            var text = await response.Content.ReadAsStringAsync();
            return ((int)response.StatusCode, string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text));
        }

        public void Dispose() => _http.Dispose();
    }
}
