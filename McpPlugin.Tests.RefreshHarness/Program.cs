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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.McpPlugin.AgentConfig;

namespace com.IvanMurzak.McpPlugin.Tests.RefreshHarness
{
    /// <summary>
    /// Subprocess harness for the mixed-language refresh concurrency suite (unified-machine-auth
    /// 04 §5 — real OS processes, not threads; task x2). Spawned by the cross-language driver in
    /// AI-Game-Dev-CLI-Core (<c>test/concurrency-suite.subprocess.test.ts</c>) next to Node
    /// worker processes, all contending on ONE shared machine credential store against a local
    /// fake authorization server. Every event is a machine-parseable stdout line, flushed
    /// immediately.
    ///
    /// <para>The store runs in the internal plaintext-for-testing mode (the C# counterpart of the
    /// TS <c>identityCredentialCodec</c>) so both languages share one at-rest format; DPAPI
    /// cross-implementation parity is proven separately (task x1).</para>
    ///
    /// <para>Commands:</para>
    /// <list type="bullet">
    ///   <item><c>hammer &lt;baseDir&gt; &lt;serverBase&gt; &lt;clientId&gt; &lt;skewMs&gt;
    ///         &lt;loopDelayMs&gt; &lt;maxDurationMs&gt; &lt;stopFile&gt;</c> — the REAL stack
    ///         (<see cref="PluginCredentialProvider"/> over the public
    ///         <see cref="MachineCredentialLock"/> with the real 15/60/75 s contract constants),
    ///         looping <see cref="PluginCredentialProvider.GetAccessTokenAsync"/> until the stop
    ///         file appears. Exit 0 = clean; 5 = the family died (sign-in required).</item>
    ///   <item><c>hammer-nolock …same args…</c> — the LOCK-DISABLED plant composition: the same
    ///         real store + real <see cref="HttpTokenRefresher"/>, driven directly WITHOUT the
    ///         cross-process lock (read → refresh → write, racing everyone). Exists to prove the
    ///         suite goes red / redundant when the lock control is removed (04 §5 mandated
    ///         plants). Exit 5 on invalid_grant (the family-revoke signal the plant watches
    ///         for).</item>
    ///   <item><c>once &lt;baseDir&gt; &lt;serverBase&gt; &lt;clientId&gt;
    ///         [--scaled t,s,b]</c> — a single forced
    ///         <see cref="PluginCredentialProvider.RefreshAsync"/>. The kill-victim and the
    ///         lost-response retry of the D10 grace-window plant. <c>--scaled</c> shrinks the
    ///         HTTP-timeout / lock-stale / acquire-budget trio (internal test ctors) so a crashed
    ///         peer's orphan lock is taken over WITHIN the 30 s grace window; the ordering
    ///         invariant <c>timeout &lt; stale &lt; budget</c> is asserted before use (04 §2 —
    ///         scaled-down variants must preserve it). Exit 0 = refreshed; 5 = sign-in required;
    ///         7 = attempt failed.</item>
    /// </list>
    /// </summary>
    internal static class Program
    {
        private const int ExitOk = 0;
        private const int ExitUsage = 1;
        private const int ExitDead = 5;
        private const int ExitOnceFailed = 7;

        private static long NowUnixMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private static void Report(string line)
        {
            Console.Out.WriteLine(line);
            Console.Out.Flush(); // the parent driver reacts to lines in real time — never buffer
        }

        private static async Task<int> Main(string[] args)
        {
            try
            {
                if (args.Length < 4)
                    return Usage();

                var command = args[0];
                var baseDir = args[1];
                var serverBase = args[2];
                var clientId = args[3];

                switch (command)
                {
                    case "hammer":
                    case "hammer-nolock":
                        if (args.Length != 8)
                            return Usage();
                        return await Hammer(
                            baseDir, serverBase, clientId,
                            skewMs: int.Parse(args[4]),
                            loopDelayMs: int.Parse(args[5]),
                            maxDurationMs: int.Parse(args[6]),
                            stopFile: args[7],
                            useLock: command == "hammer").ConfigureAwait(false);
                    case "once":
                        string? scaled = null;
                        if (args.Length == 6 && args[4] == "--scaled")
                            scaled = args[5];
                        else if (args.Length != 4)
                            return Usage();
                        return await Once(baseDir, serverBase, clientId, scaled).ConfigureAwait(false);
                    default:
                        Console.Error.WriteLine("unknown command: " + command);
                        return ExitUsage;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERR " + ex);
                return ExitUsage;
            }
        }

        private static int Usage()
        {
            Console.Error.WriteLine(
                "usage: hammer|hammer-nolock <baseDir> <serverBase> <clientId> <skewMs> <loopDelayMs> <maxDurationMs> <stopFile>\n" +
                "       once <baseDir> <serverBase> <clientId> [--scaled <httpTimeoutMs>,<staleMs>,<budgetMs>]");
            return ExitUsage;
        }

        /// <summary>Counts successful refresh responses so the driver can attribute per-language
        /// rotation work from the <c>DONE refreshes=…</c> line (the fake AS's counters stay the
        /// authoritative cross-check).</summary>
        private sealed class CountingRefresher : ITokenRefresher
        {
            private readonly ITokenRefresher _inner;
            private int _successCount;

            public CountingRefresher(ITokenRefresher inner)
            {
                _inner = inner;
            }

            public int SuccessCount => Volatile.Read(ref _successCount);

            public Task<TokenRefreshResult> RefreshAsync(string refreshToken, string? serverTarget, CancellationToken cancellationToken = default)
                => CountAsync(_inner.RefreshAsync(refreshToken, serverTarget, cancellationToken));

            public Task<TokenRefreshResult> RefreshAsync(TokenRefreshRequest request, CancellationToken cancellationToken = default)
                => CountAsync(_inner.RefreshAsync(request, cancellationToken));

            private async Task<TokenRefreshResult> CountAsync(Task<TokenRefreshResult> attempt)
            {
                var result = await attempt.ConfigureAwait(false);
                if (result != null && result.Succeeded)
                    Interlocked.Increment(ref _successCount);
                return result!;
            }
        }

        private static async Task<int> Hammer(
            string baseDir, string serverBase, string clientId,
            int skewMs, int loopDelayMs, int maxDurationMs, string stopFile, bool useLock)
        {
            var store = new MachineCredentialStore(baseDir, plaintextForTesting: true);
            var counting = new CountingRefresher(new HttpTokenRefresher(serverBase, clientId));

            Report("READY t=" + NowUnixMs + " pid=" + Environment.ProcessId + " mode=" + (useLock ? "hammer" : "hammer-nolock"));

            var deadline = DateTimeOffset.UtcNow.AddMilliseconds(maxDurationMs);
            string? lastToken = null;
            var distinct = 0;

            if (useLock)
            {
                using var provider = new PluginCredentialProvider(
                    store,
                    counting,
                    logger: null,
                    refreshSkew: TimeSpan.FromMilliseconds(skewMs),
                    clock: null,
                    credentialLock: new MachineCredentialLock(baseDir));

                while (DateTimeOffset.UtcNow < deadline && !File.Exists(stopFile))
                {
                    var token = await provider.GetAccessTokenAsync(CancellationToken.None).ConfigureAwait(false);
                    if (provider.State.CurrentValue == AuthState.SignInRequired)
                    {
                        Report("GRANT-DEAD t=" + NowUnixMs + " refreshes=" + counting.SuccessCount);
                        return ExitDead;
                    }
                    if (token == null)
                    {
                        Report("GRANT-DEAD t=" + NowUnixMs + " reason=no-token refreshes=" + counting.SuccessCount);
                        return ExitDead;
                    }
                    if (token != lastToken)
                    {
                        lastToken = token;
                        distinct++;
                        Report("TOKEN t=" + NowUnixMs + " v=" + token);
                    }
                    await Task.Delay(loopDelayMs).ConfigureAwait(false);
                }

                Report("DONE t=" + NowUnixMs + " refreshes=" + counting.SuccessCount + " distinct=" + distinct);
                return ExitOk;
            }

            // Lock-DISABLED composition (mandated plant): real store + real refresher, no mutual
            // exclusion. Re-reads immediately before each attempt (mirroring the provider's
            // re-read-first discipline) so the raced presents are predecessors, not stale
            // generations — the case the D10 grace window is specified to absorb.
            while (DateTimeOffset.UtcNow < deadline && !File.Exists(stopFile))
            {
                var read = store.TryRead();
                if (read.Status != MachineCredentialStoreStatus.Ok || read.Credentials == null)
                {
                    // Transient mid-replace read or a vanished store — retry next cycle.
                    await Task.Delay(loopDelayMs).ConfigureAwait(false);
                    continue;
                }

                var credentials = read.Credentials;
                var family = credentials.Families?.Plugin ?? credentials.Families?.Legacy;
                if (family?.RefreshToken == null)
                {
                    Report("GRANT-DEAD t=" + NowUnixMs + " reason=no-refresh-token refreshes=" + counting.SuccessCount);
                    return ExitDead;
                }

                var withinSkew = family.ExpiresAt == null
                    || family.ExpiresAt.Value - DateTimeOffset.UtcNow <= TimeSpan.FromMilliseconds(skewMs);
                if (withinSkew)
                {
                    var request = new TokenRefreshRequest(family.RefreshToken!, serverBase, family.ClientId);
                    var result = await counting.RefreshAsync(request, CancellationToken.None).ConfigureAwait(false);
                    if (result.Succeeded)
                    {
                        family.AccessToken = result.AccessToken;
                        family.RefreshToken = string.IsNullOrEmpty(result.RefreshToken) ? family.RefreshToken : result.RefreshToken;
                        family.ExpiresAt = result.ExpiresAt;
                        store.Write(credentials); // NO LOCK — the control this plant removes
                        if (result.AccessToken != lastToken)
                        {
                            lastToken = result.AccessToken;
                            distinct++;
                            Report("TOKEN t=" + NowUnixMs + " v=" + result.AccessToken);
                        }
                    }
                    else if (result.FailureKind == TokenRefreshFailureKind.InvalidGrant)
                    {
                        Report("GRANT-DEAD t=" + NowUnixMs + " reason=" + (result.FailureReason ?? "invalid_grant") + " refreshes=" + counting.SuccessCount);
                        return ExitDead;
                    }
                    // Transient failures: retry next cycle.
                }

                await Task.Delay(loopDelayMs).ConfigureAwait(false);
            }

            Report("DONE t=" + NowUnixMs + " refreshes=" + counting.SuccessCount + " distinct=" + distinct);
            return ExitOk;
        }

        private static async Task<int> Once(string baseDir, string serverBase, string clientId, string? scaled)
        {
            var store = new MachineCredentialStore(baseDir, plaintextForTesting: true);

            HttpTokenRefresher httpRefresher;
            MachineCredentialLock credentialLock;
            if (scaled != null)
            {
                var parts = scaled.Split(',');
                if (parts.Length != 3)
                    return Usage();
                var timeoutMs = int.Parse(parts[0]);
                var staleMs = int.Parse(parts[1]);
                var budgetMs = int.Parse(parts[2]);

                // 04 §2: a scaled-down variant is acceptable ONLY when the ordering invariant
                // (HTTP timeout < lock-stale < acquire-budget) is preserved — assert it.
                if (!(timeoutMs < staleMs && staleMs < budgetMs))
                    throw new InvalidOperationException(
                        "scaled constants must preserve the ordering invariant timeout < stale < budget, got: " + scaled);

                httpRefresher = new HttpTokenRefresher(
                    serverBase, clientId, httpClient: null, clock: null, attemptWindow: null, timeoutMs: timeoutMs);
                credentialLock = new MachineCredentialLock(
                    baseDir, hostId: null, acquireBudgetMs: budgetMs, staleMs: staleMs,
                    foreignStaleMs: MachineCredentialLock.FOREIGN_HOST_STALE_MS);
            }
            else
            {
                httpRefresher = new HttpTokenRefresher(serverBase, clientId);
                credentialLock = new MachineCredentialLock(baseDir);
            }

            var counting = new CountingRefresher(httpRefresher);
            using var provider = new PluginCredentialProvider(
                store, counting, logger: null, refreshSkew: null, clock: null, credentialLock: credentialLock);

            Report("READY t=" + NowUnixMs + " pid=" + Environment.ProcessId + " mode=once" + (scaled != null ? "-scaled" : ""));

            var ok = await provider.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
            Report("ONCE t=" + NowUnixMs + " ok=" + (ok ? "true" : "false") + " refreshes=" + counting.SuccessCount);

            if (provider.State.CurrentValue == AuthState.SignInRequired)
                return ExitDead;
            return ok ? ExitOk : ExitOnceFailed;
        }
    }
}
