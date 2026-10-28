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
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server.Tools
{
    /// <summary>
    /// The code prefixes of the tool-call errors the server itself produces. Agents and CLIs may key
    /// retries on them: <see cref="PluginReloading"/>, and <see cref="PluginReconnected"/> for a
    /// read-only tool, are safe to retry.
    /// </summary>
    public static class ToolCallErrors
    {
        /// <summary>A required argument is missing or an argument has the wrong JSON type (HTTP 400).</summary>
        public const string InvalidParams = "[invalid_params]";

        /// <summary>An argument holds a lone UTF-16 surrogate (HTTP 400; JSON-RPC -32602 over MCP).</summary>
        public const string InvalidUtf16 = "[invalid_utf16]";

        /// <summary>The whole call exceeded the server's tool timeout (HTTP 504).</summary>
        public const string ToolTimeout = "[tool_timeout]";

        /// <summary>The connection dropped after the call was sent and the plugin is back; not re-sent, may have run (HTTP 502).</summary>
        public const string PluginReconnected = "[plugin_reconnected]";

        /// <summary>As <see cref="PluginReconnected"/>, but the plugin did not come back within the reconnect wait (HTTP 502).</summary>
        public const string PluginDisconnected = "[plugin_disconnected]";

        /// <summary>The plugin refused the call while reloading; not re-sent (HTTP 503).</summary>
        public const string PluginReloading = "[plugin_reloading]";
    }

    /// <summary>
    /// What <see cref="RemoteToolRunner"/> checks around sending a tool call:
    /// <list type="number">
    ///   <item><b>Arguments.</b> Lone UTF-16 surrogates and arguments that do not fit the tool's input
    ///   schema (<see cref="ToolArgumentValidator"/>) are refused before the call is sent.</item>
    ///   <item><b>One deadline.</b> The whole call, including waiting for a reconnecting plugin and a
    ///   deferred ("processing") completion, ends with <see cref="ToolCallErrors.ToolTimeout"/> after the
    ///   tool timeout.</item>
    ///   <item><b>Plugin reloads.</b> A call is sent again only when the plugin answered that it did not
    ///   execute it (<see cref="ResponseCallTool.NotExecuted"/>). Any other failure during a reload (a
    ///   reload-gate refusal, a connection that dropped after the call was sent) is not re-sent: the server
    ///   waits for the plugin to come back and says so in the error, so the agent can check and retry.</item>
    /// </list>
    /// </summary>
    internal sealed class ToolCallGuard
    {
        /// <summary>Text of a plugin reload-gate refusal (Godot-MCP). Not proof the call did not start.</summary>
        internal const string ReloadRefusalMarker = "this call was not run";

        static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

        /// <summary>A call refused as not executed is sent again at least this often while the plugin stays connected.</summary>
        static readonly TimeSpan RefusalRetryInterval = TimeSpan.FromSeconds(2);

        /// <summary>After a failure, a target that is still the same connection this long is taken as never having left.</summary>
        static readonly TimeSpan SameConnectionSettle = TimeSpan.FromSeconds(2);

        /// <summary>The longest the tool list for argument validation may take; validation is skipped after it.</summary>
        static readonly TimeSpan ValidationListTimeout = TimeSpan.FromSeconds(5);

        /// <summary>A cached schema that rejects a call is checked against a fresh list when it is older than this.</summary>
        static readonly TimeSpan SchemaRecheckAge = TimeSpan.FromSeconds(5);

        const int MaxCachedTargets = 64;

        readonly IMcpConnectionStrategy _strategy;
        readonly Func<string, CancellationToken, Task<ResponseListTool[]?>> _fetchTools;
        readonly ILogger _logger;
        readonly TimeSpan _toolTimeout;
        readonly TimeSpan _reconnectWait;

        /// <summary>Input schemas per plugin connection, from the last tool list that connection answered.</summary>
        readonly ConcurrentDictionary<string, SchemaSnapshot> _schemas = new ConcurrentDictionary<string, SchemaSnapshot>(StringComparer.Ordinal);

        /// <param name="fetchTools">Asks one plugin connection for its tools (null when it does not answer).</param>
        public ToolCallGuard(IMcpConnectionStrategy strategy, Func<string, CancellationToken, Task<ResponseListTool[]?>> fetchTools,
            TimeSpan toolTimeout, TimeSpan reconnectWait, ILogger logger)
        {
            _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            _fetchTools = fetchTools ?? throw new ArgumentNullException(nameof(fetchTools));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _toolTimeout = toolTimeout > TimeSpan.Zero ? toolTimeout : TimeSpan.FromMinutes(5);
            _reconnectWait = reconnectWait < TimeSpan.Zero ? TimeSpan.Zero : reconnectWait;
        }

        /// <summary>Runs <paramref name="send"/> (one delivery of the call) under the checks above.</summary>
        public async Task<ResponseData<ResponseCallTool>> CallAsync(RequestCallTool request,
            Func<CancellationToken, Task<ResponseData<ResponseCallTool>>> send, CancellationToken cancellationToken)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(_toolTimeout);
            var elapsed = Stopwatch.StartNew();
            var sent = false;

            try
            {
                var invalid = FindInvalidText(request) ?? await ValidateArgumentsAsync(request, deadline.Token);
                if (invalid != null)
                    return Fail(request, invalid, ResponseErrorKind.BadRequest, 400);

                var resendDeadline = elapsed.Elapsed + _reconnectWait;
                while (true)
                {
                    var target = CurrentTarget();
                    sent = true;
                    var response = await send(deadline.Token);

                    if (IsServerTimeout(response) || (response.Status != ResponseStatus.Success && IsDeadline(deadline, cancellationToken)))
                        return TimedOut(request, sent, response.Message);

                    if (IsNotExecuted(response) && elapsed.Elapsed < resendDeadline)
                    {
                        _logger.LogInformation("Tool '{tool}': the plugin did not execute it ({message}); sending it again once the plugin is back.", request.Name, response.Message);
                        await WaitForReconnectAsync(target, RefusalRetryInterval, deadline.Token);
                        continue;
                    }

                    if (IsReloadRefusal(response))
                    {
                        var window = RemainingWindow(elapsed);
                        var back = await WaitForReconnectAsync(target, window, deadline.Token);
                        return Fail(request, back
                            ? $"{ToolCallErrors.PluginReloading} The engine plugin refused '{request.Name}' because it was reloading, and is connected again: " +
                              $"retry the call now (it was not re-sent automatically). Plugin: {response.Message}"
                            : $"{ToolCallErrors.PluginReloading} The engine plugin refused '{request.Name}' because it was reloading, and did not reconnect " +
                              $"within {window.TotalSeconds:0} s. Retry once the editor has finished reloading. Plugin: {response.Message}",
                            ResponseErrorKind.Unavailable, ResponseCallTool.NotExecutedStatusCode);
                    }

                    if (IsMaybeDelivered(response))
                    {
                        var window = RemainingWindow(elapsed);
                        var back = await WaitForReconnectAsync(target, window, deadline.Token);
                        return Fail(request, back
                            ? $"{ToolCallErrors.PluginReconnected} The engine plugin disconnected while running '{request.Name}' (usually a script or " +
                              "assembly reload) and is connected again. The call was not re-sent because it may already have run: " +
                              "check its effect, then retry it if it did not happen (a read-only call can be retried at once)."
                            : $"{ToolCallErrors.PluginDisconnected} The engine plugin disconnected while running '{request.Name}' and did not reconnect " +
                              $"within {window.TotalSeconds:0} s. The call may already have run. Check that the editor is open and connected, then retry.",
                            ResponseErrorKind.Unavailable, ClientUtils.MaybeDeliveredStatusCode);
                    }

                    return response;
                }
            }
            catch (OperationCanceledException) when (IsDeadline(deadline, cancellationToken))
            {
                return TimedOut(request, sent, null);
            }
        }

        static bool IsDeadline(CancellationTokenSource deadline, CancellationToken callerToken)
            => deadline.IsCancellationRequested && !callerToken.IsCancellationRequested;

        /// <summary>The reconnect wait, cut to what is left of the tool timeout.</summary>
        TimeSpan RemainingWindow(Stopwatch elapsed)
        {
            var left = _toolTimeout - elapsed.Elapsed;
            if (left < TimeSpan.Zero)
                left = TimeSpan.Zero;
            return _reconnectWait < left ? _reconnectWait : left;
        }

        // ───────────────────────────── arguments ─────────────────────────────

        /// <summary>Caches the input schemas of a tool list the plugin on <paramref name="connectionId"/> answered.</summary>
        public void Remember(string connectionId, ResponseListTool[]? tools) => RememberSnapshot(connectionId, tools);

        /// <summary>
        /// Validates against the input schemas the current plugin connection listed (listed now when it has
        /// not yet). A cached schema that rejects the call is checked against a fresh list first unless it is
        /// recent: a tool may change without a reconnect. Without a list from this connection the plugin
        /// validates on its own.
        /// </summary>
        async Task<string?> ValidateArgumentsAsync(RequestCallTool request, CancellationToken cancellationToken)
        {
            var target = CurrentTarget();
            if (target == null)
                return null; // no plugin right now: the call waits for one, and the plugin validates

            var fresh = false;
            if (!_schemas.TryGetValue(target, out var schemas))
            {
                schemas = await RefreshSchemasAsync(target, cancellationToken);
                fresh = true;
            }

            var problem = Check(schemas, request);
            if (problem == null || fresh || schemas == null || schemas.Age.Elapsed < SchemaRecheckAge)
                return problem;

            return Check(await RefreshSchemasAsync(target, cancellationToken), request);
        }

        static string? Check(SchemaSnapshot? schemas, RequestCallTool request)
            => schemas != null && schemas.ByName.TryGetValue(request.Name, out var schema)
                ? ToolArgumentValidator.Validate(request.Name, schema, request.Arguments)
                : null; // a tool this connection did not list: the plugin answers that itself

        async Task<SchemaSnapshot?> RefreshSchemasAsync(string target, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ValidationListTimeout);
            try
            {
                return RememberSnapshot(target, await _fetchTools(target, timeout.Token));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Validation is best effort: without a schema the plugin checks the arguments itself.
                _logger.LogDebug(ex, "Could not list tools to validate arguments.");
                return null;
            }
        }

        SchemaSnapshot? RememberSnapshot(string connectionId, ResponseListTool[]? tools)
        {
            if (string.IsNullOrEmpty(connectionId) || tools == null)
                return null;
            var byName = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var tool in tools)
            {
                if (tool?.Name != null && tool.InputSchema.ValueKind == JsonValueKind.Object)
                    byName[tool.Name] = tool.InputSchema.Clone();
            }
            if (_schemas.Count >= MaxCachedTargets)
                _schemas.Clear(); // old connections: dropping them all is simpler than tracking disconnects
            var snapshot = new SchemaSnapshot(byName, Stopwatch.StartNew());
            _schemas[connectionId] = snapshot;
            return snapshot;
        }

        sealed class SchemaSnapshot
        {
            public SchemaSnapshot(IReadOnlyDictionary<string, JsonElement> byName, Stopwatch age)
            {
                ByName = byName;
                Age = age;
            }

            public IReadOnlyDictionary<string, JsonElement> ByName { get; }
            public Stopwatch Age { get; }
        }

        /// <summary>An error for the first argument (or the tool name) holding a lone UTF-16 surrogate, or null.</summary>
        internal static string? FindInvalidText(RequestCallTool request)
        {
            if (Utf16Validation.HasLoneSurrogate(request.Name))
                return Utf16Validation.Describe("name");
            if (request.Arguments == null)
                return null;
            foreach (var argument in request.Arguments)
            {
                var path = Utf16Validation.HasLoneSurrogate(argument.Key)
                    ? argument.Key
                    : Utf16Validation.Find(argument.Value, argument.Key);
                if (path != null)
                    return Utf16Validation.Describe($"arguments.{path}") + $" (tool '{request.Name}')";
            }
            return null;
        }

        // ───────────────────────────── reloads ─────────────────────────────

        /// <summary>The library's own timeouts (no answer, or no deferred completion); a tool's own timeout error is left alone.</summary>
        static bool IsServerTimeout(ResponseData<ResponseCallTool> response)
            => response.Status == ResponseStatus.Error
                && response.ErrorKind == ResponseErrorKind.Timeout
                && response.Message?.IndexOf("not re-sent", StringComparison.Ordinal) >= 0;

        static bool IsNotExecuted(ResponseData<ResponseCallTool> response)
            => response.Status == ResponseStatus.Error
                && response.ErrorKind == ResponseErrorKind.Unavailable
                && response.HttpStatusCode == ResponseCallTool.NotExecutedStatusCode;

        /// <summary>A reload-gate refusal without the not-executed status; not one for a plugin that is shutting down.</summary>
        static bool IsReloadRefusal(ResponseData<ResponseCallTool> response)
            => response.Status == ResponseStatus.Error
                && response.ErrorKind == ResponseErrorKind.Unavailable
                && response.HttpStatusCode is null
                && response.Message is { } message
                && message.IndexOf(ReloadRefusalMarker, StringComparison.OrdinalIgnoreCase) >= 0
                && message.IndexOf("shutting down", StringComparison.OrdinalIgnoreCase) < 0;

        static bool IsMaybeDelivered(ResponseData<ResponseCallTool> response)
            => response.Status == ResponseStatus.Error
                && response.ErrorKind == ResponseErrorKind.Unavailable
                && response.HttpStatusCode == ClientUtils.MaybeDeliveredStatusCode;

        string? CurrentTarget() => _strategy.ResolveConnectionId(McpSessionTokenContext.CurrentToken, 0);

        /// <summary>
        /// Waits up to <paramref name="window"/> for the plugin to be connected through a different connection
        /// than <paramref name="previous"/> (or at all, when there was none). A connection that is still
        /// <paramref name="previous"/> after <see cref="SameConnectionSettle"/> never left: true as well.
        /// </summary>
        async Task<bool> WaitForReconnectAsync(string? previous, TimeSpan window, CancellationToken cancellationToken)
        {
            var waited = Stopwatch.StartNew();
            while (true)
            {
                var current = CurrentTarget();
                if (current != null && (previous == null || !string.Equals(current, previous, StringComparison.Ordinal)))
                    return true;
                if (waited.Elapsed >= window)
                    return false;
                if (current != null && waited.Elapsed >= SameConnectionSettle)
                    return true;
                await Task.Delay(PollInterval, cancellationToken);
            }
        }

        // ───────────────────────────── errors ─────────────────────────────

        ResponseData<ResponseCallTool> TimedOut(RequestCallTool request, bool sent, string? detail)
        {
            var message = $"{ToolCallErrors.ToolTimeout} Tool '{request.Name}' did not finish within {_toolTimeout.TotalMilliseconds:0} ms (the server's tool timeout). " +
                (sent
                    ? "It was not re-sent; the engine may still be running it, or its main thread may be blocked (a modal dialog, an endless loop, a long import). "
                    : "It was not sent: the engine plugin did not answer in time. ") +
                "Raise the limit with --tool-timeout=<ms> or MCP_PLUGIN_TOOL_TIMEOUT.";
            if (!string.IsNullOrEmpty(detail))
                message += " Detail: " + detail;
            return Fail(request, message, ResponseErrorKind.Timeout, 504);
        }

        ResponseData<ResponseCallTool> Fail(RequestCallTool request, string message, ResponseErrorKind kind, int httpStatusCode)
        {
            _logger.LogWarning("Tool '{tool}' ({requestId}): {message}", request.Name, request.RequestID, message);
            return ResponseCallTool.Error(message, kind, httpStatusCode).Pack(request.RequestID);
        }
    }
}
