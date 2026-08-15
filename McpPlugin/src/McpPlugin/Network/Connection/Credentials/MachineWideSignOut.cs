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
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.AgentConfig;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>The outcome of a machine-wide sign-out (see <see cref="MachineWideSignOut"/>).</summary>
    public sealed class MachineSignOutResult
    {
        internal MachineSignOutResult(bool storeDeleted, bool busy, int familiesRevoked, int revocationsFailed, string? detail)
        {
            StoreDeleted = storeDeleted;
            Busy = busy;
            FamiliesRevoked = familiesRevoked;
            RevocationsFailed = revocationsFailed;
            Detail = detail;
        }

        /// <summary>True when the credential store file was deleted (the machine is signed out).</summary>
        public bool StoreDeleted { get; }

        /// <summary>True when the lock-protocol delete path was busy — the store is untouched; retry.</summary>
        public bool Busy { get; }

        /// <summary>How many families the server acknowledged revoking.</summary>
        public int FamiliesRevoked { get; }

        /// <summary>
        /// How many families could not be revoked after bounded retries (offline sign-out — F6.4:
        /// the store is still deleted; unrevoked families die naturally at ≤30 d and remain listed
        /// on the website until then).
        /// </summary>
        public int RevocationsFailed { get; }

        /// <summary>Human-facing detail. Never token material.</summary>
        public string? Detail { get; }
    }

    /// <summary>
    /// Machine-wide sign-out (unified-machine-auth design 03 F6, task b3): best-effort RFC 7009
    /// revocation of EVERY stored family's refresh token — the agent and plugin families with their
    /// stored <c>clientId</c>s, a legacy family with the component-default id (F6.2) — each with
    /// bounded retries, followed by the 04 §2 lock-protocol delete path (acquire → unlink store →
    /// release → unlink lock). Revocation failures never block the local sign-out (F6.4); a busy
    /// lock DOES (the store must never be unlinked outside the lock protocol).
    /// </summary>
    public static class MachineWideSignOut
    {
        /// <summary>Bounded revocation retries per family (best-effort — F6).</summary>
        public const int RevokeAttemptsPerFamily = 3;

        /// <summary>Backoff between revocation retries, in ms (kept short — sign-out is user-facing).</summary>
        public const int RevokeRetryDelayMs = 250;

        /// <summary>
        /// Run the full sequence. <paramref name="componentClientId"/> is this component's own
        /// OAuth client id, presented when revoking a legacy family of unknown id (F6.2 —
        /// RFC 7009 answers 200 even for a mismatched id/unknown token, so this can never make
        /// sign-out worse). An unreadable store skips revocation (its tokens cannot be read) but is
        /// still deleted: sign-out is the explicit user action that authorizes replacing/removing
        /// an unreadable store (04 §1).
        /// </summary>
        public static async Task<MachineSignOutResult> SignOutAsync(
            MachineCredentialStore store,
            MachineCredentialLock credentialLock,
            ITokenRevocationClient revocationClient,
            string componentClientId,
            ILogger? logger = null,
            CancellationToken cancellationToken = default)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (credentialLock == null) throw new ArgumentNullException(nameof(credentialLock));
            if (revocationClient == null) throw new ArgumentNullException(nameof(revocationClient));
            if (string.IsNullOrEmpty(componentClientId))
                throw new ArgumentException("The component's own client id is required (legacy-family revocation, F6.2).", nameof(componentClientId));

            // ── Server-side revocation FIRST (03 F6.2) — before the tokens are unlinked locally. ──
            var revoked = 0;
            var failed = 0;
            var read = store.TryRead();
            if (read.Status == MachineCredentialStoreStatus.Ok && read.Credentials != null)
            {
                var credentials = read.Credentials;
                var serverTarget = credentials.ServerTarget;
                foreach (var target in EnumerateRevocationTargets(credentials, componentClientId))
                {
                    if (await RevokeWithRetriesAsync(revocationClient, target.Key, target.Value, serverTarget, logger, cancellationToken).ConfigureAwait(false))
                        revoked++;
                    else
                        failed++;
                }
            }
            else if (read.Status == MachineCredentialStoreStatus.Unreadable)
            {
                logger?.LogWarning("Sign-out: credential store is unreadable ({error}); skipping server-side revocation and deleting locally.", read.Error);
            }

            // ── Lock-protocol delete path (04 §2): acquire → unlink store → release. ─────────────
            var deleted = await Task.Run(() => credentialLock.TryDeleteStore(store), cancellationToken).ConfigureAwait(false);
            if (!deleted)
            {
                return new MachineSignOutResult(storeDeleted: false, busy: true, revoked, failed,
                    "credential lock busy — the store was not deleted; retry sign-out");
            }

            return new MachineSignOutResult(storeDeleted: true, busy: false, revoked, failed,
                failed > 0 ? "some families could not be revoked server-side; they expire naturally (≤30 d)" : null);
        }

        /// <summary>
        /// The refresh tokens to revoke, paired with the client id to present: each family's stored
        /// <c>clientId</c>; the component default for a legacy family (unknown by definition, F6.2).
        /// </summary>
        static IEnumerable<KeyValuePair<string, string>> EnumerateRevocationTargets(MachineCredentials credentials, string componentClientId)
        {
            var families = credentials.Families;
            if (families == null)
                yield break;

            if (families.Agent?.RefreshToken != null)
                yield return new KeyValuePair<string, string>(families.Agent.RefreshToken!, families.Agent.ClientId ?? componentClientId);
            if (families.Plugin?.RefreshToken != null)
                yield return new KeyValuePair<string, string>(families.Plugin.RefreshToken!, families.Plugin.ClientId ?? componentClientId);
            if (families.Legacy?.RefreshToken != null)
                yield return new KeyValuePair<string, string>(families.Legacy.RefreshToken!, families.Legacy.ClientId ?? componentClientId);
        }

        static async Task<bool> RevokeWithRetriesAsync(
            ITokenRevocationClient revocationClient,
            string refreshToken,
            string clientId,
            string? serverTarget,
            ILogger? logger,
            CancellationToken cancellationToken)
        {
            for (var attempt = 1; attempt <= RevokeAttemptsPerFamily; attempt++)
            {
                try
                {
                    if (await revocationClient.RevokeAsync(refreshToken, clientId, serverTarget, cancellationToken).ConfigureAwait(false))
                        return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger?.LogWarning("Token revocation attempt {attempt} threw: {message}", attempt, ex.Message); // never token material
                }

                if (attempt < RevokeAttemptsPerFamily)
                    await Task.Delay(RevokeRetryDelayMs, cancellationToken).ConfigureAwait(false);
            }
            return false;
        }
    }
}
