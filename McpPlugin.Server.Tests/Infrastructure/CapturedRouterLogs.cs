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
using System.Threading;
using NLog;
using NLog.Config;
using NLog.Targets;
using NLogLevel = NLog.LogLevel;

namespace com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure
{
    /// <summary>
    /// Captures what a server-side router writes to the operator log, so a test can assert on it.
    ///
    /// <para>Why this exists at all: the routers log through NLog's STATIC
    /// <see cref="LogManager"/>, not through the host's Microsoft.Extensions.Logging pipeline, so
    /// they are unaffected by <c>builder.Logging.ClearProviders()</c> AND invisible to any MEL
    /// provider. <c>McpPlugin.Tests/Infrastructure/TestLoggerFactory</c> is a MEL provider in a
    /// DIFFERENT assembly and cannot observe these writes — a test wired to it asserts on nothing
    /// and stays green when the log line under test is deleted. Capture on the NLog side instead:
    /// that is what this type does.</para>
    ///
    /// <para><b>Global state, and how this stays safe under xUnit parallelism.</b>
    /// <see cref="LogManager.Configuration"/> is process-global, and only about two thirds of this
    /// assembly's test classes carry <c>[Collection("McpPlugin.Server")]</c> — the rest genuinely
    /// DO run concurrently — so the collection attribute alone cannot make this safe. Three
    /// mechanisms do, and none of them is optional:</para>
    /// <list type="number">
    /// <item><description><b>Assembly-wide mutual exclusion.</b> Every install passes through the
    /// static <see cref="_gate"/> semaphore and holds it until <see cref="Dispose"/>. Two captures
    /// therefore never overlap, so no capture can observe a configuration another one installed,
    /// and the saved "previous" configuration is never a configuration some other capture is still
    /// using. Without this, a second install would silently drop the first one's rule (a whole-config
    /// swap) and the two disposals would restore in an order that leaves the wrong config
    /// installed.</description></item>
    /// <item><description><b>Logger-name scoping.</b> The installed rule matches only the router
    /// type's own logger name (<c>LogManager.GetCurrentClassLogger()</c> names a logger after its
    /// declaring type), so unrelated concurrent tests can neither pollute the capture nor read
    /// it.</description></item>
    /// <item><description><b>Exact restore.</b> <see cref="Dispose"/> puts back the very
    /// <see cref="LoggingConfiguration"/> instance that was installed beforehand (including
    /// <c>null</c>, the usual state in this suite) and is idempotent, so a double-dispose cannot
    /// over-release the gate.</description></item>
    /// </list>
    ///
    /// <para>Callers should ALSO carry <c>[Collection("McpPlugin.Server")]</c>. That is a
    /// throughput measure rather than a correctness one: it keeps log-asserting classes from
    /// queueing on the gate and burning xUnit worker threads while they block.</para>
    ///
    /// <para>Usage:
    /// <code>
    /// using var logs = CapturedRouterLogs.InstallFor(typeof(PromptRouter));
    /// // ... drive the degraded path ...
    /// logs.Text.ShouldContain(expectedFailureText, Case.Sensitive);
    /// </code>
    /// The argument is a <see cref="Type"/> rather than a generic parameter because the routers are
    /// STATIC classes, which C# forbids as type arguments.</para>
    /// </summary>
    public sealed class CapturedRouterLogs : IDisposable
    {
        /// <summary>
        /// Assembly-wide: at most one capture may own <see cref="LogManager.Configuration"/> at a
        /// time. Deliberately NOT reentrant — a nested install is a bug, and blocking surfaces it.
        /// </summary>
        static readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        /// <summary>
        /// How long <see cref="InstallFor(Type)"/> waits for the gate before giving up. Generous
        /// enough for a slow end-to-end test to finish, short enough that a leaked capture fails
        /// with a named error instead of hanging the suite until xUnit's own timeout.
        /// </summary>
        public static readonly TimeSpan DefaultAcquireTimeout = TimeSpan.FromSeconds(60);

        readonly LoggingConfiguration? _previous;
        readonly MemoryTarget _target;
        int _disposed;

        CapturedRouterLogs(Type routerType, NLogLevel minLevel)
        {
            _previous = LogManager.Configuration;
            _target = new MemoryTarget($"router-log-capture-{routerType.Name}") { Layout = "${message}" };

            var config = new LoggingConfiguration();
            // Derived from the type, not spelled out: a router's logger name IS its full type name
            // (LogManager.GetCurrentClassLogger), so a rename cannot silently stop matching.
            config.AddRule(minLevel, NLogLevel.Fatal, _target, routerType.FullName!);
            LogManager.Configuration = config;
        }

        /// <summary>
        /// Installs a capture of everything <paramref name="routerType"/> logs at Warn or above —
        /// the operator-visible band.
        /// </summary>
        /// <param name="routerType">The router whose logger to capture, e.g. <c>typeof(PromptRouter)</c>.</param>
        public static CapturedRouterLogs InstallFor(Type routerType)
            => InstallFor(routerType, NLogLevel.Warn);

        /// <summary>Installs a capture from <paramref name="minLevel"/> up to Fatal.</summary>
        public static CapturedRouterLogs InstallFor(Type routerType, NLogLevel minLevel)
        {
            var captured = TryInstallFor(routerType, minLevel, DefaultAcquireTimeout);
            if (captured == null)
            {
                throw new TimeoutException(
                    $"Timed out after {DefaultAcquireTimeout.TotalSeconds}s waiting for the " +
                    $"{nameof(CapturedRouterLogs)} gate while installing a capture for " +
                    $"'{routerType.FullName}'. Another capture is still installed — it was most " +
                    "likely not disposed (always use `using var`).");
            }

            return captured;
        }

        /// <summary>
        /// Gate-aware install that gives up instead of throwing. Exposed for the tests that pin the
        /// mutual-exclusion guarantee itself; ordinary callers want <see cref="InstallFor(Type)"/>.
        /// </summary>
        internal static CapturedRouterLogs? TryInstallFor(Type routerType, NLogLevel minLevel, TimeSpan acquireTimeout)
        {
            if (routerType == null)
                throw new ArgumentNullException(nameof(routerType));
            if (minLevel == null)
                throw new ArgumentNullException(nameof(minLevel));

            if (!_gate.Wait(acquireTimeout))
                return null;

            try
            {
                return new CapturedRouterLogs(routerType, minLevel);
            }
            catch
            {
                _gate.Release();
                throw;
            }
        }

        /// <summary>Everything captured so far, newest last, one entry per log call.</summary>
        public IReadOnlyList<string> Lines => _target.Logs.ToArray();

        /// <summary>The captured entries joined into one blob, for substring assertions.</summary>
        public string Text => string.Join(Environment.NewLine, _target.Logs);

        public void Dispose()
        {
            // Idempotent: a second Dispose must not release the gate a second time, which would let
            // two captures run concurrently and reintroduce exactly the race this type prevents.
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            LogManager.Configuration = _previous;
            _gate.Release();
        }
    }
}
