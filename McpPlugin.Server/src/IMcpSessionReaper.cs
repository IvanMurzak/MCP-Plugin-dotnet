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

namespace com.IvanMurzak.McpPlugin.Server
{
    /// <summary>
    /// A session's hold on one installation identity slot. Disposing it releases the slot — but only if
    /// this session still owns it (see <see cref="IMcpSessionReaper.Claim"/>).
    /// </summary>
    public interface IMcpSessionIdentityLease : IDisposable
    {
        /// <summary>The MCP session id that holds the slot.</summary>
        string McpSessionId { get; }

        /// <summary>
        /// True once a LATER connection with the same identity took this slot away. The displaced
        /// session's own unwinding is then an expected, orderly shutdown rather than a fault — which is
        /// why it is logged at debug and not as an error.
        /// </summary>
        bool Displaced { get; }

        /// <summary>
        /// The eviction of the predecessor this claim displaced, or <c>null</c> when there was none
        /// (the overwhelmingly common case: a first connection). Its
        /// <see cref="SdkEvictionHandle.Outcome"/> is already decided when <see cref="IMcpSessionReaper.Claim"/>
        /// returns; its <see cref="SdkEvictionHandle.DisposeCompletion"/> may still be running.
        /// </summary>
        SdkEvictionHandle? DisplacedPredecessor { get; }
    }

    /// <summary>
    /// The <b>replace-by-identity</b> rule (design 07 §2.3 / D10.2): when an installation that already
    /// has a live MCP session connects again with the same identity, the previous session is
    /// <b>replaced</b> rather than <b>stacked</b>.
    ///
    /// <para><b>The case this exists for is the one nothing else can reach.</b> A cleanly-closing client
    /// sends an MCP <c>DELETE</c> and the SDK reaps its session. A client that <b>crashed or was
    /// force-killed</b> sends nothing, by construction — so no client-side change can ever reap those
    /// sessions, and today they survive until the production idle timeout of 21600 s (6 h). That is the
    /// pile-up this rule ends: the next <c>initialize</c> from the same installation reaps its own
    /// predecessor.</para>
    ///
    /// <para><b>Why <c>initialize</c> creates a new entry today.</b>
    /// <c>McpServerService.StartAsync</c> composes its tracker key as
    /// <c>"&lt;accountSub&gt;:&lt;mcpSessionId&gt;"</c>, and the MCP session id is freshly minted by every
    /// <c>initialize</c>. So the key is different every time by construction, and sessions can only ever
    /// stack. Replacing needs a key that is stable across reconnects — which is exactly what the
    /// installation identity supplies, and why it had to be introduced.</para>
    ///
    /// <para><b>The key, and a deliberate deviation worth reading.</b> The slot is keyed on
    /// <c>(accountSub, instanceId, projectPin)</c>. The task specification for this change says
    /// <c>(accountSub, instanceId)</c>; <c>07-security.md</c> (§2.5 copied-installation row, threat T25,
    /// gate SG-14) says <c>(accountSub, instanceId, <b>pin</b>)</c> and gives the reason: a home directory
    /// copied by a VM image or a OneDrive sync genuinely shares one <c>installId</c>, so two clones
    /// working on <i>different projects</i> would otherwise evict each other forever in a mutual flap.
    /// Including the pin is a strict superset of the two-part key — same account + same installation +
    /// same project still replaces, and two different accounts still cannot touch each other — so it
    /// satisfies the task's requirements as written and additionally honours SG-14. Dropping the pin would
    /// satisfy the task and violate the design.</para>
    ///
    /// <para><b>What is NOT a key.</b> <c>accountSub</c> comes from the validated bearer credential
    /// (<c>ConnectionIdentity.FromPrincipal</c>) and never from a client-supplied body field, so the
    /// client-supplied <c>instanceId</c> can only ever select among sessions <i>inside</i> the account the
    /// credential already proved. A spoofed instance id evicts only the spoofer's own sessions; it cannot
    /// name, reach, or reason about another account's.</para>
    ///
    /// <para><b>Retention (pinned here for the privacy clause).</b> The transmitted HMAC is held in this
    /// object's in-memory dictionary and <b>nowhere else</b>: not in a database, not on disk, not in a
    /// webhook payload, and not in a log line (logs carry a truncated, non-reversible fingerprint — see
    /// <c>InstanceIdentityHeader.Fingerprint</c>). The entry is removed when the session that claimed it
    /// ends, in the same <c>finally</c> that removes the session's tracker row, or when a newer session
    /// with the same identity replaces it, or when the process exits. <b>It therefore never outlives the
    /// session it keys, and there is no retention period to state because nothing is retained.</b> The one
    /// exception holds no instance id: the session id and account of a REPLACED session are kept in memory
    /// for <c>McpSessionReaper.ReplacedSessionRetention</c> (see <see cref="WasReplaced"/>).</para>
    /// </summary>
    public interface IMcpSessionReaper
    {
        /// <summary>
        /// Claims the identity slot for <paramref name="mcpSessionId"/>, replacing and terminating any
        /// prior holder of the same <c>(accountSub, instanceId, projectPin)</c>.
        ///
        /// <para>Returns <c>null</c> — claiming nothing, replacing nothing — when
        /// <paramref name="accountSub"/> or <paramref name="instanceId"/> is null/empty. <b>That null is
        /// the compatibility path, and it is load-bearing:</b> every currently released client sends no
        /// instance id, so it must land here and behave exactly as it does today. No replace, no
        /// rejection, no warning.</para>
        ///
        /// <para>The SDK-side removal of the predecessor happens <b>before this method returns</b>, so on
        /// return the old session id is gone from this process's session store. With a session-migration
        /// handler registered, a later request carrying that id is rehydrated, without a slot (see
        /// <see cref="ClaimIfVacant"/>). Only the predecessor's resource dispose may
        /// still be in flight; await <see cref="IMcpSessionIdentityLease.DisplacedPredecessor"/>'s
        /// <see cref="SdkEvictionHandle.DisposeCompletion"/> if you need it finished.</para>
        /// </summary>
        IMcpSessionIdentityLease? Claim(string? accountSub, string? instanceId, string? projectPin, string mcpSessionId);

        /// <summary>
        /// Claims the identity slot for a session the SDK <b>rehydrated</b> from the session-migration store
        /// (a request carried an <c>Mcp-Session-Id</c> this process does not hold), but only when the slot is
        /// free. It never displaces a live holder; in that case it returns <c>null</c> and the session runs
        /// without a slot.
        ///
        /// <para><b>Why rehydration must not displace.</b> The migration record of a session displaced by
        /// <see cref="Claim"/> outlives the displacement, so the displaced client's next request revives it.
        /// If that revival displaced in turn, two live clients of one installation and project (two chats of
        /// one App) would evict each other on every request, each eviction cancelling the other's in-flight
        /// request: the production ping-pong of 2026-11-10. Only a real <c>initialize</c> displaces, which
        /// keeps the case this rule exists for — a crashed client's session is reaped by that client's next
        /// <c>initialize</c>, and a crashed client never sends a request carrying its old id.</para>
        ///
        /// <para><b>The trade-off.</b> A rehydrated session that found the slot taken holds no slot, so a later
        /// <c>initialize</c> of the same identity does not reap it. It lives until the client sends
        /// <c>DELETE</c> or the SDK's idle timeout prunes it, exactly like a session of a client that sends no
        /// identity at all. After a restart the slots are empty, so the first rehydrated session of each
        /// identity claims its slot as before.</para>
        /// </summary>
        IMcpSessionIdentityLease? ClaimIfVacant(string? accountSub, string? instanceId, string? projectPin, string mcpSessionId);

        /// <summary>Number of identity slots currently held. Zero when no client sends an instance id.</summary>
        int TrackedIdentityCount { get; }

        /// <summary>
        /// The MCP session id currently holding a slot, or <c>null</c>. Diagnostic: it is what lets a test
        /// assert that two accounts presenting the SAME instance id hold two independent slots.
        /// </summary>
        string? CurrentSessionIdFor(string? accountSub, string? instanceId, string? projectPin);

        /// <summary>
        /// Whether <paramref name="mcpSessionId"/> of <paramref name="accountSub"/> was displaced by a newer
        /// <see cref="Claim"/> recently (a few minutes). A request that still carries such an id comes from a
        /// client that is alive while another live client presents the same identity — a duplicated identity,
        /// not a crash, since a crashed client sends nothing. The server tells that client so with the
        /// <c>AI-Game-Dev-Session-Replaced</c> response header (see <c>McpSessionTokenMiddleware</c>). Only a
        /// header, not a status: a session-migration store may still serve the replaced id, and released clients
        /// rely on that (see <see cref="ClaimIfVacant"/>).
        ///
        /// <para>Only the session id and the account are remembered, never the instance id, and only for
        /// the retention window. Another account presenting the id learns nothing.</para>
        /// </summary>
        bool WasReplaced(string? mcpSessionId, string? accountSub) => false;
    }
}
