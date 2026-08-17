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
using com.IvanMurzak.McpPlugin.Server.Auth;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server
{
    /// <summary>
    /// In-memory implementation of the replace-by-identity rule. See <see cref="IMcpSessionReaper"/> for
    /// the rule, the key shape, and the retention statement — this file is the mechanics.
    /// </summary>
    public sealed class McpSessionReaper : IMcpSessionReaper
    {
        /// <summary>
        /// The slot key. A <c>record</c> so equality is structural, and every component is compared with
        /// the default string comparer, i.e. <b>ordinal</b> — never culture-sensitive. Culture-sensitive
        /// comparison on an account id or a hex token is how two distinct identities become one.
        /// </summary>
        sealed record IdentityKey(string AccountSub, string InstanceId, string? ProjectPin);

        readonly ILogger<McpSessionReaper>? _logger;
        readonly IMcpSdkSessionEvictor _evictor;

        // Value is the lease object itself, so releasing can be an ATOMIC compare-and-remove on reference
        // identity. See Lease.Dispose for why anything weaker is wrong.
        readonly ConcurrentDictionary<IdentityKey, Lease> _slots = new ConcurrentDictionary<IdentityKey, Lease>();

        // Serialises claim-and-displace. The window it closes is real: two initialize requests for the
        // same identity arriving concurrently could otherwise both read "no predecessor", both install
        // themselves, and leave one session live but unreferenced — a leaked session that no future
        // replace can ever reach, which is the exact failure this class exists to prevent.
        readonly object _claimLock = new object();

        public McpSessionReaper(IMcpSdkSessionEvictor evictor, ILogger<McpSessionReaper>? logger = null)
        {
            _evictor = evictor ?? throw new ArgumentNullException(nameof(evictor));
            _logger = logger;
        }

        public int TrackedIdentityCount => _slots.Count;

        public string? CurrentSessionIdFor(string? accountSub, string? instanceId, string? projectPin)
        {
            if (string.IsNullOrEmpty(accountSub) || string.IsNullOrEmpty(instanceId))
                return null;
            return _slots.TryGetValue(new IdentityKey(accountSub!, instanceId!, projectPin), out var lease)
                ? lease.McpSessionId
                : null;
        }

        public IMcpSessionIdentityLease? Claim(string? accountSub, string? instanceId, string? projectPin, string mcpSessionId)
        {
            // ── The compatibility path. Every released client lands here. ──────────────────────────────
            // No identity ⇒ no slot, no replace, no rejection, no log noise. Behaviour is byte-for-byte
            // today's. This is checked FIRST and returns null rather than, say, keying on an empty string:
            // an empty-string key would silently pool every anonymous client into ONE slot, and each new
            // anonymous connection would then evict an unrelated user's session. That bug would look
            // exactly like the feature working.
            if (string.IsNullOrEmpty(accountSub) || string.IsNullOrEmpty(instanceId))
                return null;

            if (string.IsNullOrEmpty(mcpSessionId))
                return null;

            var key = new IdentityKey(accountSub!, instanceId!, projectPin);
            var lease = new Lease(this, key, mcpSessionId);

            // The same-session check lives INSIDE the lock, together with MarkDisplaced. It used to sit
            // after the lock, which meant a same-session re-claim marked the OUTGOING lease `Displaced`
            // before discovering it was the same session — so that session's handler would later report an
            // ordinary cancellation as "displaced by replace-by-identity". Cosmetic, but it is exactly the
            // kind of wrong attribution an operator would then chase.
            Lease? predecessor = null;
            var sameSessionReclaim = false;
            lock (_claimLock)
            {
                if (_slots.TryGetValue(key, out var existing))
                {
                    if (string.Equals(existing.McpSessionId, mcpSessionId, StringComparison.Ordinal))
                    {
                        // Same session re-claiming its own slot. Nothing to terminate — terminating here
                        // would kill the live session that just asked to be kept — and nothing to flag.
                        sameSessionReclaim = true;
                    }
                    else
                    {
                        predecessor = existing;
                        // Flag BEFORE evicting: the displaced session's handler observes cancellation as a
                        // direct consequence of the eviction below, and it must already be able to tell
                        // that its shutdown is an orderly replace rather than a fault.
                        predecessor.MarkDisplaced();
                    }
                }

                _slots[key] = lease;
            }

            if (sameSessionReclaim)
            {
                _logger?.LogDebug(
                    "MCP identity slot re-claimed by the same session. Instance: {instanceFingerprint}, SessionId: {sessionId}.",
                    InstanceIdentityHeader.Fingerprint(instanceId), mcpSessionId);
                return lease;
            }

            if (predecessor == null)
            {
                _logger?.LogDebug(
                    "MCP identity slot claimed. Instance: {instanceFingerprint}, Pin: {pin}, SessionId: {sessionId}, Slots: {slots}.",
                    InstanceIdentityHeader.Fingerprint(instanceId), projectPin ?? "none", mcpSessionId, _slots.Count);
                return lease;
            }

            var eviction = _evictor.Evict(predecessor.McpSessionId);
            lease.SetDisplacedPredecessor(eviction);

            // The instance id NEVER appears here — only its truncated fingerprint — and this line carries
            // no credential, so the id is never logged beside the bearer header.
            _logger?.LogInformation(
                "MCP session replaced by identity. Instance: {instanceFingerprint}, Pin: {pin}, " +
                "ReplacedSessionId: {replacedSessionId}, NewSessionId: {newSessionId}, Eviction: {outcome}, Slots: {slots}.",
                InstanceIdentityHeader.Fingerprint(instanceId), projectPin ?? "none",
                predecessor.McpSessionId, mcpSessionId, eviction.Outcome, _slots.Count);

            return lease;
        }

        /// <summary>
        /// Releases a slot, but ONLY if <paramref name="lease"/> still holds it.
        ///
        /// <para>The conditional matters and is not defensive padding. A displaced session unwinds
        /// <i>after</i> its successor has already installed itself, and its <c>finally</c> disposes its own
        /// lease. An unconditional <c>TryRemove(key)</c> would then delete the SUCCESSOR's slot, leaving a
        /// live session that no later connection can find and therefore can never replace — the leak, back,
        /// reachable only through the code that was added to fix it. <c>ConcurrentDictionary</c>'s
        /// key/value <c>TryRemove</c> overload does the compare-and-remove atomically on reference identity.</para>
        /// </summary>
        void Release(Lease lease)
        {
            var removed = ((ICollection<KeyValuePair<IdentityKey, Lease>>)_slots)
                .Remove(new KeyValuePair<IdentityKey, Lease>(lease.Key, lease));

            _logger?.LogDebug(
                "MCP identity slot release. Instance: {instanceFingerprint}, SessionId: {sessionId}, " +
                "Released: {released}, Displaced: {displaced}, Slots: {slots}.",
                InstanceIdentityHeader.Fingerprint(lease.Key.InstanceId), lease.McpSessionId,
                removed, lease.Displaced, _slots.Count);
        }

        sealed class Lease : IMcpSessionIdentityLease
        {
            readonly McpSessionReaper _owner;
            int _disposed;
            volatile bool _displaced;
            volatile SdkEvictionHandle? _displacedPredecessor;

            internal Lease(McpSessionReaper owner, IdentityKey key, string mcpSessionId)
            {
                _owner = owner;
                Key = key;
                McpSessionId = mcpSessionId;
            }

            internal IdentityKey Key { get; }

            public string McpSessionId { get; }

            public bool Displaced => _displaced;

            public SdkEvictionHandle? DisplacedPredecessor => _displacedPredecessor;

            internal void MarkDisplaced() => _displaced = true;

            internal void SetDisplacedPredecessor(SdkEvictionHandle handle) => _displacedPredecessor = handle;

            public void Dispose()
            {
                if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;
                _owner.Release(this);
            }
        }
    }
}
