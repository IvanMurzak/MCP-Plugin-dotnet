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
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests.IsolationMatrix
{
    /// <summary>
    /// The mcp-authorize Phase-2 GATE (task b8, design 04 + 07): the live multi-tenant isolation
    /// matrix that proves McpPlugin 7.0 end-to-end before the host (c1) and engines (Phase 4) consume
    /// it. It composes the SAME server pairing plane the DemoWebApp boots (<c>AccountMcpStrategy</c> +
    /// its <c>AccountInstances</c> registry + the server-native selection tools + the ambient
    /// <see cref="McpSessionTokenContext"/> request context), and drives the full
    /// <b>two accounts × two plugin instances × two MCP sessions</b> grid across every plane:
    /// <list type="bullet">
    ///   <item><b>Isolation</b> — a session of one account can never route to, be notified about, or
    ///   observe another account's instances (fail closed).</item>
    ///   <item><b>Selection</b> — <c>select_engine_instance</c> is per-session, never per-account, and
    ///   can never reach a foreign account's instance.</item>
    ///   <item><b>Strict pin</b> — a pinned session routes ONLY to its project and never falls through
    ///   to another project (even the account's sole live instance), and account scoping dominates a
    ///   pin that happens to match a foreign project.</item>
    /// </list>
    /// It is <b>security-critical</b>: the harness asserts isolation, not merely the happy path — the
    /// <c>LeakDetector_*</c> tests deliberately introduce cross-account routing / notification leakage
    /// and prove the matrix's own invariant checks FAIL on it, so a real regression cannot pass CI.
    ///
    /// <para>Origin-403 (all modes) and stdio same-project contention — the transport-security legs of
    /// the b8 matrix — run over a REAL loopback host / REAL sockets in
    /// <see cref="Phase2GateTransportMatrixTests"/> (sibling file). The suite is deliberately
    /// hermetic (no external AS, no multi-process orchestration) so it is a stable CI gate; see the
    /// runnable wrapper <c>scripts/phase2-gate-matrix.sh</c> / <c>.ps1</c> to invoke it standalone.</para>
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class Phase2GateIsolationMatrixTests : IDisposable
    {
        // ── The two fake accounts (the JWT `sub` — THE routing key) ──────────────────────────────
        const string AccountA = "acc-A-sub";
        const string AccountB = "acc-B-sub";

        // ── Two projects for account A (full SHA-256 hex + its 8-hex-char routing pin) ────────────
        const string HashA1 = "a1a1a1a1deadbeefcafef00d00112233445566778899aabbccddeeff001122334";
        const string PinA1 = "a1a1a1a1";
        const string HashA2 = "a2a2a2a2feedface0f0f0f0faabbccdd112233445566778899aabbccddeeff001";
        const string PinA2 = "a2a2a2a2";

        // ── One project for account B (its hash must never be reachable from account A) ───────────
        const string HashB = "bbbbbbbb00998877665544332211ffeeddccbbaa00112233445566778899aabbc";
        const string PinB = "bbbbbbbb";

        // A pin that matches NO connected instance of account A (its project's editor is closed).
        const string PinAClosed = "deadc0de";

        readonly AccountMcpStrategy _strategy;
        readonly AccountInstances _registry;
        readonly SessionSelectionStore _selections;
        readonly ServerNativeTools _tools;

        // The live world: connection ids per instance (the SignalR connection each plugin holds).
        readonly PluginInstance _iA1;   // account A, unity:GameA1
        readonly PluginInstance _iA2;   // account A, godot:GameA2
        readonly PluginInstance _iB1;   // account B, unity:GameB

        public Phase2GateIsolationMatrixTests()
        {
            _registry = new AccountInstances();
            _strategy = new AccountMcpStrategy(_registry);
            _selections = new SessionSelectionStore();
            _tools = new ServerNativeTools(_registry, _selections, new NoOpEnrollment());

            // Register the 2 accounts × their plugin instances — the "two fake plugin instances" per
            // the b8 matrix live on account A; account B is the isolation counterpart.
            _iA1 = _strategy.RegisterInstance(PluginId(AccountA), Meta("iA1", "unity", "GameA1", HashA1, "PC-A1"), "conn-A1", NullLogger.Instance);
            _iA2 = _strategy.RegisterInstance(PluginId(AccountA), Meta("iA2", "godot", "GameA2", HashA2, "PC-A2"), "conn-A2", NullLogger.Instance);
            _iB1 = _strategy.RegisterInstance(PluginId(AccountB), Meta("iB1", "unity", "GameB", HashB, "PC-B1"), "conn-B1", NullLogger.Instance);
        }

        public void Dispose() => ClearAmbient();

        // ════════════════════════════════ Isolation (the security core) ════════════════════════════════

        /// <summary>
        /// The full grid: every MCP session of every account, in every pin/selection state, resolves
        /// ONLY to an instance of its own account — never crosses a tenant boundary. This is the
        /// composed b8 matrix (2 accounts × 2 A-instances × 2 sessions × the selection/pin states).
        /// </summary>
        [Fact]
        public void Matrix_EveryCell_RoutesOnlyWithinItsOwnAccount()
        {
            var cellsChecked = 0;

            foreach (var session in AllGridSessions())
            {
                using (Enter(session))
                {
                    var resolvedConn = _strategy.ResolveConnectionId(token: "ignored", retryOffset: 0);

                    // A resolved connection MUST belong to the session's own account. A null resolution
                    // (pin-no-match / account-empty) is also isolation-safe — it never crosses tenants.
                    if (resolvedConn != null)
                    {
                        var owningAccount = _registry.GetAccountByConnection(resolvedConn);
                        owningAccount.ShouldBe(session.AccountId,
                            $"[{session.Label}] routed to a connection owned by '{owningAccount}', not the session's account '{session.AccountId}' — TENANT LEAK");
                    }

                    cellsChecked++;
                }
            }

            // Guard against a vacuous pass: the grid must actually enumerate a non-trivial matrix.
            cellsChecked.ShouldBeGreaterThanOrEqualTo(10, "the isolation matrix must enumerate a real grid of cells");
        }

        /// <summary>
        /// Account B's session, whatever its pin/selection, can NEVER reach account A's instances —
        /// and account A's session can never reach account B's single instance. Fail closed: the
        /// foreign resolution is either null or, if resolved, only ever its OWN account's instance.
        /// </summary>
        [Fact]
        public void Matrix_ForeignAccountSession_NeverReachesOtherTenantsInstance()
        {
            // B agent, unpinned → resolves ONLY B's own instance, never conn-A1/conn-A2.
            using (Enter(Session.Agent(AccountB, "sB1")))
            {
                var conn = _strategy.ResolveConnectionId("ignored", 0);
                conn.ShouldBe("conn-B1");
                conn.ShouldNotBe("conn-A1");
                conn.ShouldNotBe("conn-A2");
            }

            // B agent PINNED to account A's real project pin → still fails closed. Account scoping is
            // evaluated first; the pin can never bridge accounts even when it matches a foreign hash.
            using (Enter(Session.Agent(AccountB, "sB2", pin: PinA1)))
            {
                _strategy.ResolveConnectionId("ignored", 0).ShouldBeNull("a pin must never cross the account boundary");
            }

            // A agent can never reach B's instance either.
            using (Enter(Session.Agent(AccountA, "sA1", pin: PinB)))
            {
                _strategy.ResolveConnectionId("ignored", 0).ShouldBeNull("account A pinned to B's project must fail closed");
            }
        }

        /// <summary>
        /// Notification scoping across the grid: an account's lifecycle event targets ONLY that
        /// account's live plugin connections, and a plugin's list-changed notification reaches a
        /// session only when the session's account owns that plugin. Never cross-tenant, never a broadcast.
        /// </summary>
        [Fact]
        public void Matrix_Notifications_ScopeToOwningAccountOnly()
        {
            // Account A event → exactly A's two connections; never B's. (Two instances ⇒ SpecificMany.)
            var targetA = _strategy.ResolveNotificationTarget(AccountA);
            targetA.Kind.ShouldBe(NotificationTarget.TargetKind.SpecificMany);
            AllTargetConnections(targetA).ShouldBe(new[] { "conn-A1", "conn-A2" }, ignoreOrder: true);
            AllTargetConnections(targetA).ShouldNotContain("conn-B1");

            // Account B event → exactly B's single connection; never A's. (One instance ⇒ collapses to Specific.)
            var targetB = _strategy.ResolveNotificationTarget(AccountB);
            targetB.Kind.ShouldBe(NotificationTarget.TargetKind.Specific);
            AllTargetConnections(targetB).ShouldBe(new[] { "conn-B1" });
            AllTargetConnections(targetB).ShouldNotContain("conn-A1");
            AllTargetConnections(targetB).ShouldNotContain("conn-A2");

            // ShouldNotifySession is symmetric-scoped: A's plugin notifies only A's session.
            _strategy.ShouldNotifySession("conn-A1", sessionId: AccountA).ShouldBeTrue();
            _strategy.ShouldNotifySession("conn-A1", sessionId: AccountB).ShouldBeFalse();
            _strategy.ShouldNotifySession("conn-B1", sessionId: AccountA).ShouldBeFalse();

            // Never broadcasts, for any probe.
            foreach (var probe in new[] { (string?)null, "", AccountA, AccountB, "acc-unknown" })
                _strategy.ResolveNotificationTarget(probe).Kind.ShouldNotBe(NotificationTarget.TargetKind.Broadcast);
        }

        /// <summary>
        /// Account-scoped session DATA: a plugin connection observes only its own account's session
        /// data; an unregistered/foreign connection observes nothing (denies unscoped access).
        /// </summary>
        [Fact]
        public void Matrix_SessionData_ScopesByAccount()
        {
            _registry.GetAccountByConnection("conn-A1").ShouldBe(AccountA);
            _registry.GetAccountByConnection("conn-B1").ShouldBe(AccountB);
            _registry.GetAccountByConnection("conn-does-not-exist").ShouldBeNull();

            // The registry never leaks one account's instance list into another's view.
            _registry.GetInstances(AccountA).Select(i => i.ConnectionId).ShouldBe(new[] { "conn-A1", "conn-A2" }, ignoreOrder: true);
            _registry.GetInstances(AccountB).Select(i => i.ConnectionId).ShouldBe(new[] { "conn-B1" });
            _registry.GetInstances("acc-unknown").ShouldBeEmpty();
        }

        // ════════════════════════════════ Leak detector — proves the matrix has teeth ════════════════════════════════

        /// <summary>
        /// The security-critical proof (b8 DoD "assert isolation, not just happy path"): if the routing
        /// plane leaked account A's session onto account B's instance, the matrix's own invariant check
        /// (<see cref="AssertRouteStaysInAccount"/>) MUST fail. We feed the check a deliberately-leaky
        /// resolution and assert it throws — so a real cross-account regression cannot slip through green.
        /// </summary>
        [Fact]
        public void LeakDetector_RoutingLeak_TripsTheInvariantCheck()
        {
            // A hypothetical buggy strategy routes account A's session to account B's connection.
            const string leakedConnection = "conn-B1"; // owned by account B

            Should.Throw<Exception>(() =>
                AssertRouteStaysInAccount(sessionAccount: AccountA, resolvedConnectionId: leakedConnection),
                "the isolation matrix must FAIL when a session is routed to another account's instance");

            // Control: the same check PASSES for a correct, same-account route.
            Should.NotThrow(() =>
                AssertRouteStaysInAccount(sessionAccount: AccountA, resolvedConnectionId: "conn-A1"));
        }

        /// <summary>
        /// The notification counterpart: a notification target that (buggily) contains a foreign
        /// account's connection must trip the matrix's cross-tenant notification check.
        /// </summary>
        [Fact]
        public void LeakDetector_NotificationLeak_TripsTheInvariantCheck()
        {
            // A correct account-A target contains only A's connections.
            var correct = new[] { "conn-A1", "conn-A2" };
            Should.NotThrow(() => AssertNotificationStaysInAccount(AccountA, correct));

            // A leaky target smuggles in account B's connection — must be caught.
            var leaky = new[] { "conn-A1", "conn-B1" };
            Should.Throw<Exception>(() => AssertNotificationStaysInAccount(AccountA, leaky),
                "the isolation matrix must FAIL when an account's notification target includes another tenant's connection");
        }

        // ════════════════════════════════ Selection (per-session, never cross-account) ════════════════════════════════

        /// <summary>
        /// <c>select_engine_instance</c> is per-SESSION: session sA1 selecting iA2 routes sA1 to iA2,
        /// while a second session sA2 of the SAME account is unaffected (independent selection).
        /// </summary>
        [Fact]
        public async Task Selection_IsPerSession_NotPerAccount()
        {
            // sA1 selects iA2 (godot:GameA2).
            using (Enter(Session.Agent(AccountA, "sA1")))
            {
                var res = await _tools.HandleAsync(ServerNativeTools.SelectInstance,
                    Args(("instance_id", "iA2")), CtxOf("sA1", AccountA));
                res.Status.ShouldBe(ResponseStatus.Success);
            }

            // sA1 now routes to iA2 (sticky selection honored on subsequent requests).
            using (Enter(Session.Agent(AccountA, "sA1", selected: "iA2")))
            {
                _strategy.ResolveConnectionId("ignored", 0).ShouldBe("conn-A2");
            }

            // sA2 (same account, no selection) is independent — its stored selection is null.
            _selections.Get("sA2").ShouldBeNull();
            _selections.Get("sA1").ShouldBe("iA2");
        }

        /// <summary>
        /// A session can never select a foreign account's instance — the selection tool only sees the
        /// caller account's registry bucket, so account A selecting iB1 (account B) is rejected.
        /// </summary>
        [Fact]
        public async Task Selection_CannotTargetForeignAccountInstance()
        {
            using (Enter(Session.Agent(AccountA, "sA1")))
            {
                var res = await _tools.HandleAsync(ServerNativeTools.SelectInstance,
                    Args(("instance_id", "iB1")), CtxOf("sA1", AccountA));
                res.Status.ShouldBe(ResponseStatus.Error);
                res.GetMessage().ShouldNotBeNull();
            }

            // And list_engine_instances only ever shows the caller's own instances.
            using (Enter(Session.Agent(AccountA, "sA1")))
            {
                var list = await _tools.HandleAsync(ServerNativeTools.ListInstances, NoArgs, CtxOf("sA1", AccountA));
                var text = list.GetMessage() ?? string.Empty;
                text.ShouldContain("iA1");
                text.ShouldContain("iA2");
                text.ShouldNotContain("iB1"); // never a foreign account's instance
            }
        }

        // ════════════════════════════════ Strict pin (D14) ════════════════════════════════

        /// <summary>
        /// A pinned session routes ONLY to the matching project. When the pinned project's editor is
        /// closed the pin NEVER falls through to the account's other live instance — it fails closed
        /// with the pinned-no-match variant.
        /// </summary>
        [Fact]
        public void Pin_RoutesOnlyToMatchingProject_NeverFallsThrough()
        {
            // Pinned to project A1 → routes to iA1 only.
            using (Enter(Session.Agent(AccountA, "sA1", pin: PinA1)))
            {
                _strategy.ResolveConnectionId("ignored", 0).ShouldBe("conn-A1");
            }

            // Pinned to project A2 → routes to iA2 only.
            using (Enter(Session.Agent(AccountA, "sA1", pin: PinA2)))
            {
                _strategy.ResolveConnectionId("ignored", 0).ShouldBe("conn-A2");
            }

            // Pinned to a project whose editor is CLOSED, while the account HAS other live instances →
            // never falls through; the resolution is the pinned-no-match variant, routing resolves null.
            using (Enter(Session.Agent(AccountA, "sA1", pin: PinAClosed)))
            {
                _strategy.ResolveCurrentSession().Kind.ShouldBe(InstanceResolutionKind.NoMatchPinned);
                _strategy.ResolveConnectionId("ignored", 0).ShouldBeNull();
            }
        }

        /// <summary>
        /// The strict-pin corner the design calls out explicitly: a pin never falls through EVEN when
        /// the mismatched instance is the account's ONLY live instance. We drop account A to a single
        /// instance (iA1) and pin to A2 — resolution must still be pinned-no-match, not the sole instance.
        /// </summary>
        [Fact]
        public void Pin_NeverFallsThrough_EvenToTheSoleLiveInstance()
        {
            _strategy.OnPluginDisconnected(typeof(McpServerHub), "conn-A2", NullLogger.Instance); // now only iA1 remains for A
            _registry.InstanceCount(AccountA).ShouldBe(1);

            using (Enter(Session.Agent(AccountA, "sA1", pin: PinA2))) // A2's editor is gone
            {
                _strategy.ResolveCurrentSession().Kind.ShouldBe(InstanceResolutionKind.NoMatchPinned);
                _strategy.ResolveConnectionId("ignored", 0).ShouldBeNull("a pin must never fall through to a different project, even the sole instance");
            }
        }

        /// <summary>
        /// A sticky selection may narrow a pin but can never override it to a DIFFERENT project:
        /// pinned to A1, selecting iA2 is rejected by <c>select_engine_instance</c>.
        /// </summary>
        [Fact]
        public async Task Pin_SelectionCannotOverridePinToADifferentProject()
        {
            using (Enter(Session.Agent(AccountA, "sA1", pin: PinA1)))
            {
                var res = await _tools.HandleAsync(ServerNativeTools.SelectInstance,
                    Args(("instance_id", "iA2")), CtxOf("sA1", AccountA, pin: PinA1));
                res.Status.ShouldBe(ResponseStatus.Error);
                res.GetMessage()!.ShouldContain("pinned");
            }
        }

        // ════════════════════════════════ Grid + invariant helpers ════════════════════════════════

        /// <summary>
        /// The full session grid: two accounts × two A-sessions × the selection/pin states. Account B's
        /// session is the isolation counterpart. Every cell is asserted to stay in its own account.
        /// </summary>
        IEnumerable<Session> AllGridSessions()
        {
            // Account A, session sA1 — unpinned / pinned-match A1 / pinned-match A2 / pinned-no-match / selected.
            yield return Session.Agent(AccountA, "sA1");
            yield return Session.Agent(AccountA, "sA1", pin: PinA1);
            yield return Session.Agent(AccountA, "sA1", pin: PinA2);
            yield return Session.Agent(AccountA, "sA1", pin: PinAClosed);
            yield return Session.Agent(AccountA, "sA1", selected: "iA2");

            // Account A, session sA2 — unpinned / pinned-match / selected (independent from sA1).
            yield return Session.Agent(AccountA, "sA2");
            yield return Session.Agent(AccountA, "sA2", pin: PinA1);
            yield return Session.Agent(AccountA, "sA2", selected: "iA1");

            // Account B, session sB1 — unpinned / pinned-own / pinned-foreign (must never reach A).
            yield return Session.Agent(AccountB, "sB1");
            yield return Session.Agent(AccountB, "sB1", pin: PinB);
            yield return Session.Agent(AccountB, "sB1", pin: PinA1); // foreign pin — must not cross
            yield return Session.Agent(AccountB, "sB2", selected: "iB1");
        }

        /// <summary>The matrix invariant: a resolved route must stay within the session's account.</summary>
        void AssertRouteStaysInAccount(string sessionAccount, string? resolvedConnectionId)
        {
            if (resolvedConnectionId == null)
                return; // fail-closed is isolation-safe
            var owningAccount = _registry.GetAccountByConnection(resolvedConnectionId);
            owningAccount.ShouldBe(sessionAccount,
                $"route to connection '{resolvedConnectionId}' owned by '{owningAccount}' escaped the session account '{sessionAccount}'");
        }

        /// <summary>
        /// All connection ids a notification target addresses, regardless of Kind — a single-instance
        /// account collapses <c>SpecificMany</c> to <c>Specific</c> (see <see cref="NotificationTarget"/>),
        /// so both shapes must be flattened for a Kind-agnostic isolation assertion.
        /// </summary>
        static IReadOnlyList<string> AllTargetConnections(NotificationTarget target)
        {
            switch (target.Kind)
            {
                case NotificationTarget.TargetKind.Specific:
                    return new[] { target.ConnectionId! };
                case NotificationTarget.TargetKind.SpecificMany:
                    return target.ConnectionIds;
                default:
                    return Array.Empty<string>();
            }
        }

        /// <summary>The matrix invariant: an account's notification target must contain only that account's connections.</summary>
        void AssertNotificationStaysInAccount(string account, IEnumerable<string> targetConnections)
        {
            foreach (var conn in targetConnections)
            {
                var owningAccount = _registry.GetAccountByConnection(conn);
                owningAccount.ShouldBe(account,
                    $"notification target for '{account}' included connection '{conn}' owned by '{owningAccount}' — CROSS-TENANT LEAK");
            }
        }

        // ── Ambient session helpers (simulate the per-request McpSessionTokenContext AsyncLocal) ──

        readonly struct Session
        {
            public string AccountId { get; }
            public string SessionId { get; }
            public string? Pin { get; }
            public string? Selected { get; }
            public string Label => $"{AccountId}/{SessionId}{(Pin != null ? "+pin" : "")}{(Selected != null ? "+sel" : "")}";

            Session(string accountId, string sessionId, string? pin, string? selected)
            {
                AccountId = accountId;
                SessionId = sessionId;
                Pin = pin;
                Selected = selected;
            }

            public static Session Agent(string account, string sessionId, string? pin = null, string? selected = null)
                => new Session(account, sessionId, pin, selected);
        }

        IDisposable Enter(Session session)
        {
            McpSessionTokenContext.CurrentIdentity = new ConnectionIdentity(session.AccountId, ConnectionIdentity.RoleAgent);
            McpSessionTokenContext.CurrentSessionId = session.SessionId;
            McpSessionTokenContext.CurrentProjectPin = session.Pin;
            McpSessionTokenContext.CurrentSelectedInstanceId = session.Selected;
            return new AmbientReset();
        }

        sealed class AmbientReset : IDisposable
        {
            public void Dispose() => ClearAmbient();
        }

        static void ClearAmbient()
        {
            McpSessionTokenContext.CurrentIdentity = null;
            McpSessionTokenContext.CurrentSessionId = null;
            McpSessionTokenContext.CurrentProjectPin = null;
            McpSessionTokenContext.CurrentSelectedInstanceId = null;
        }

        static ConnectionIdentity PluginId(string account) => new ConnectionIdentity(account, ConnectionIdentity.RolePlugin);

        static PluginInstanceMetadata Meta(string instanceId, string engine, string project, string pathHash, string machine)
            => new PluginInstanceMetadata(instanceId, engine, project, pathHash, machine);

        static SelectionToolContext CtxOf(string sessionId, string account, string? pin = null)
            => new SelectionToolContext(account, sessionId, pin, bearer: "bearer-" + account);

        static readonly IReadOnlyDictionary<string, JsonElement> NoArgs = new Dictionary<string, JsonElement>();

        static IReadOnlyDictionary<string, JsonElement> Args(params (string key, string value)[] kv)
            => kv.ToDictionary(p => p.key, p => JsonSerializer.SerializeToElement(p.value));

        sealed class NoOpEnrollment : IEnrollmentClient
        {
            public Task<EnrollmentResult> CreateAsync(string engine, string bearer, System.Threading.CancellationToken cancellationToken = default)
                => Task.FromResult(EnrollmentResult.Ok("UNUSED"));
        }
    }
}
