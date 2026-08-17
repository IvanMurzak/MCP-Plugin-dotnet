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
    /// session it keys, and there is no retention period to state because nothing is retained.</b></para>
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
        /// return the old session id is already unreachable. Only the predecessor's resource dispose may
        /// still be in flight; await <see cref="IMcpSessionIdentityLease.DisplacedPredecessor"/>'s
        /// <see cref="SdkEvictionHandle.DisposeCompletion"/> if you need it finished.</para>
        /// </summary>
        IMcpSessionIdentityLease? Claim(string? accountSub, string? instanceId, string? projectPin, string mcpSessionId);

        /// <summary>Number of identity slots currently held. Zero when no client sends an instance id.</summary>
        int TrackedIdentityCount { get; }

        /// <summary>
        /// The MCP session id currently holding a slot, or <c>null</c>. Diagnostic: it is what lets a test
        /// assert that two accounts presenting the SAME instance id hold two independent slots.
        /// </summary>
        string? CurrentSessionIdFor(string? accountSub, string? instanceId, string? projectPin);
    }
}
