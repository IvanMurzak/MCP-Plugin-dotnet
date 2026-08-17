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
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;

namespace com.IvanMurzak.McpPlugin.Server
{
    /// <summary>
    /// Evicts an SDK-owned MCP session by performing exactly what the SDK's own <c>DELETE</c> handler
    /// performs — <c>StatefulSessionManager.TryRemove(id, out session)</c> then
    /// <c>session.DisposeAsync()</c> — reaching the manager by reflection because the type is
    /// <c>internal</c>.
    ///
    /// <para><b>Verified against <c>ModelContextProtocol.AspNetCore</c> 1.4.0</b>
    /// (<c>1.4.0+06e3604dd12fbfc08c8f9d316325380ec2a2989b</c>), by reading the tagged source AND by
    /// reflecting over the shipped IL:</para>
    /// <list type="bullet">
    /// <item><description><c>internal sealed class StatefulSessionManager</c> holds
    /// <c>ConcurrentDictionary&lt;string, StreamableHttpSession&gt; _sessions</c> and is registered as a DI
    /// singleton by <c>WithHttpTransport</c> (<c>TryAddSingleton&lt;StatefulSessionManager&gt;()</c>), so it
    /// IS resolvable — <c>IServiceProvider.GetService(Type)</c> does not require a public type.</description></item>
    /// <item><description><c>public bool TryRemove(string key, out StreamableHttpSession value)</c> is
    /// public on that internal type, hence reachable only by reflection.</description></item>
    /// <item><description><c>internal sealed class StreamableHttpSession : IAsyncDisposable</c> — the
    /// interface is PUBLIC, so once the object is in hand the dispose needs no reflection at all. Its
    /// <c>DisposeAsync</c> disposes the transport (completing the incoming channel, releasing the pending
    /// SSE <c>GET</c>, disposing the <c>SseEventWriter</c> buffer that leaked multi-GB in production),
    /// cancels the session's <c>SessionClosed</c> token, awaits <c>ServerRunTask</c>, then disposes the
    /// <c>McpServer</c>.</description></item>
    /// </list>
    ///
    /// <para><b>Cancelling <c>SessionClosed</c> is a consequence of the dispose, not a substitute for
    /// it.</b> Because <c>RunSessionHandler</c> is invoked with <c>session.SessionClosed</c> as its
    /// cancellation token, disposing the session is also what unwinds our
    /// <c>StreamableHttpTransportLayer</c> handler and therefore what removes the
    /// <see cref="IMcpSessionTracker"/> row. Doing it the other way round — cancelling our own token and
    /// hoping the SDK notices — does NOT work: nothing in 1.4.0 attaches a continuation to
    /// <c>ServerRunTask</c> that removes the session, so the entry would survive in
    /// <c>_sessions</c> until the idle reaper ran.</para>
    ///
    /// <para><b>Reflection here is a deliberate, loud dependency, not a shortcut.</b> There is no public
    /// or DI-typed API in 1.4.0 to evict a session by id, and no <c>InternalsVisibleTo</c>. The
    /// alternatives were (a) wait for the client to send <c>DELETE</c> — which is precisely what a crashed
    /// or force-killed app can never do, i.e. the case this whole rule exists for — or (b) wait for the
    /// idle reaper, i.e. keep the six-hour pile-up. So the binding is resolved ONCE, its failure is
    /// reported through <see cref="IsSupported"/>/<see cref="BindingDiagnostic"/>, logged at
    /// <c>Error</c>, and <b>asserted by a test</b> so that an SDK bump which moves this seam breaks CI
    /// instead of silently restoring the leak.</para>
    /// </summary>
    public sealed class McpSdkSessionEvictor : IMcpSdkSessionEvictor
    {
        /// <summary>Fully-qualified name of the SDK's internal session store.</summary>
        public const string SessionManagerTypeName = "ModelContextProtocol.AspNetCore.StatefulSessionManager";

        /// <summary>The method that removes a session from the store — the leak-stopping half.</summary>
        public const string TryRemoveMethodName = "TryRemove";

        // Resolved once per process against the loaded SDK assembly. A static failure here is a
        // dependency-shape change, not a per-request condition, so it is computed once and reported.
        static readonly Type? _sessionManagerType;
        static readonly MethodInfo? _tryRemove;
        static readonly string _bindingDiagnostic;

        static McpSdkSessionEvictor()
        {
            // Anchor on a PUBLIC type from the same assembly so the assembly reference is a compile-time
            // fact rather than a string to be got wrong.
            var sdkAssembly = typeof(HttpServerTransportOptions).Assembly;
            _sessionManagerType = sdkAssembly.GetType(SessionManagerTypeName, throwOnError: false);

            if (_sessionManagerType == null)
            {
                _bindingDiagnostic =
                    $"UNBOUND: type '{SessionManagerTypeName}' not found in {sdkAssembly.GetName().Name} " +
                    $"{sdkAssembly.GetName().Version}. The MCP SDK's session store moved or was renamed; " +
                    "replace-by-identity can no longer terminate SDK sessions.";
                return;
            }

            // Bind by name + parameter shape (string, out <session>) rather than by an exact type list, so
            // a rename of the SESSION type alone does not break a seam that does not care about it.
            foreach (var candidate in _sessionManagerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!string.Equals(candidate.Name, TryRemoveMethodName, StringComparison.Ordinal))
                    continue;
                if (candidate.ReturnType != typeof(bool))
                    continue;
                var parameters = candidate.GetParameters();
                if (parameters.Length != 2)
                    continue;
                if (parameters[0].ParameterType != typeof(string))
                    continue;
                if (!parameters[1].IsOut)
                    continue;
                _tryRemove = candidate;
                break;
            }

            _bindingDiagnostic = _tryRemove != null
                ? $"BOUND: {SessionManagerTypeName}.{TryRemoveMethodName}(string, out {_tryRemove.GetParameters()[1].ParameterType.Name}) " +
                  $"via {sdkAssembly.GetName().Name} {sdkAssembly.GetName().Version}; dispose via public IAsyncDisposable."
                : $"UNBOUND: '{SessionManagerTypeName}' was found but has no 'bool {TryRemoveMethodName}(string, out …)'. " +
                  "The MCP SDK changed its eviction contract; replace-by-identity can no longer terminate SDK sessions.";
        }

        readonly IServiceProvider _services;
        readonly ILogger<McpSdkSessionEvictor>? _logger;
        int _unsupportedReported;

        public McpSdkSessionEvictor(IServiceProvider services, ILogger<McpSdkSessionEvictor>? logger = null)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _logger = logger;
        }

        /// <summary>
        /// Whether the seam bound. Static, so a test can assert it without constructing a host — and that
        /// test is the tripwire for an SDK bump.
        /// </summary>
        public static bool IsBindingResolved => _tryRemove != null;

        /// <summary>Static counterpart of <see cref="BindingDiagnostic"/>. Contains no session data.</summary>
        public static string StaticBindingDiagnostic => _bindingDiagnostic;

        public bool IsSupported => _tryRemove != null;

        public string BindingDiagnostic => _bindingDiagnostic;

        public SdkEvictionHandle Evict(string mcpSessionId)
        {
            if (string.IsNullOrEmpty(mcpSessionId))
                return SdkEvictionHandle.NotFound;

            if (_tryRemove == null || _sessionManagerType == null)
            {
                ReportUnsupportedOnce();
                return SdkEvictionHandle.Unsupported;
            }

            object? manager;
            try
            {
                manager = _services.GetService(_sessionManagerType);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to resolve the MCP SDK session manager from DI. {diagnostic}", _bindingDiagnostic);
                return new SdkEvictionHandle(SdkEvictionOutcome.Failed, Task.CompletedTask);
            }

            if (manager == null)
            {
                // Reachable in a host that never called WithHttpTransport (stdio-only). Not an error there:
                // there are no HTTP sessions to evict in the first place.
                _logger?.LogDebug("MCP SDK session manager is not registered; nothing to evict. SessionId: {sessionId}.", mcpSessionId);
                return SdkEvictionHandle.NotFound;
            }

            object? session;
            try
            {
                // args[1] receives the out parameter.
                var args = new object?[] { mcpSessionId, null };
                var removed = _tryRemove.Invoke(manager, args) is bool b && b;
                session = args[1];
                if (!removed || session == null)
                    return SdkEvictionHandle.NotFound;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "MCP SDK session eviction threw. SessionId: {sessionId}. {diagnostic}", mcpSessionId, _bindingDiagnostic);
                return new SdkEvictionHandle(SdkEvictionOutcome.Failed, Task.CompletedTask);
            }

            // Removal already happened above and is what stops the leak. The dispose is the resource half;
            // it is started here and surfaced as a task so the caller can bound its wait.
            return new SdkEvictionHandle(SdkEvictionOutcome.Evicted, DisposeAsync(session, mcpSessionId));
        }

        /// <summary>
        /// Disposes an evicted session through the PUBLIC <see cref="IAsyncDisposable"/> the SDK's session
        /// type implements — no reflection needed past the lookup. Never faults the returned task: the
        /// session is already unreachable, so a dispose failure must not propagate into the caller's
        /// request path.
        /// </summary>
        async Task DisposeAsync(object session, string mcpSessionId)
        {
            try
            {
                if (session is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else if (session is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                else
                {
                    _logger?.LogWarning(
                        "Evicted MCP session is neither IAsyncDisposable nor IDisposable ({type}); its transport buffers " +
                        "will not be released until the process exits. SessionId: {sessionId}.",
                        session.GetType().FullName, mcpSessionId);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex,
                    "Disposing the evicted MCP session failed; it is already removed from the SDK session store, " +
                    "so it can no longer be reached, but its buffers may linger. SessionId: {sessionId}.",
                    mcpSessionId);
            }
        }

        void ReportUnsupportedOnce()
        {
            if (System.Threading.Interlocked.Exchange(ref _unsupportedReported, 1) != 0)
            {
                _logger?.LogDebug("MCP SDK session eviction remains unsupported. {diagnostic}", _bindingDiagnostic);
                return;
            }

            // LOUD, exactly once. This is the only signal that replace-by-identity has stopped
            // terminating SDK sessions — the same reasoning as design 07 SG-15's loud 405: a silent
            // degradation here restores the six-hour session pile-up with no functional symptom.
            _logger?.LogError(
                "MCP replace-by-identity can no longer terminate SDK-owned sessions: the eviction seam did not bind. " +
                "Displaced sessions will linger until the idle timeout prunes them. {diagnostic}",
                _bindingDiagnostic);
        }
    }
}
