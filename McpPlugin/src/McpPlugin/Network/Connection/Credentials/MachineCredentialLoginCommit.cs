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
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.AgentConfig;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>Outcome status of a <see cref="MachineCredentialLoginCommit"/> sequence.</summary>
    public enum LoginCommitStatus
    {
        /// <summary>Agent family committed, exchange succeeded, plugin family + mirror committed.</summary>
        FullyCommitted = 0,

        /// <summary>
        /// Agent family committed but the token exchange failed — the F1 failure path: the caller
        /// retries the exchange with backoff and surfaces "partially authorized, retrying". The
        /// agent family stays committed (that is why the sequence uses two separate lock holds).
        /// </summary>
        AgentOnly = 1,

        /// <summary>
        /// Agent family committed and the exchange succeeded, but the second lock hold failed
        /// (busy) — the minted plugin family is carried in
        /// <see cref="LoginCommitResult.ExchangeResult"/> so the caller can retry
        /// <see cref="MachineCredentialLoginCommit.CommitPluginFamilyAsync"/> without re-exchanging.
        /// </summary>
        PluginCommitBusy = 2,

        /// <summary>The first lock hold failed (busy) — nothing was written; retry the whole commit.</summary>
        Busy = 3,

        /// <summary>
        /// The store already belongs to a different account (<c>subject</c> mismatch — design
        /// F7/D6; the compare fires only when BOTH subjects are known) and the caller did not pass
        /// <c>confirmedReplaceOfSubject</c>. Nothing was written; the caller shows the
        /// account-switch confirm dialog and retries with the confirmed displaced subject (after
        /// best-effort revoking the displaced account's families) or aborts (revoking the
        /// just-minted tokens). Also returned when the subject CHANGED between the two holds
        /// (an interleaved login, twin reason <c>subject-changed</c>) — the second hold
        /// re-verifies the commit's premise and never writes a plugin family derived from
        /// account A into account B's store (03 F7.2); no confirmation reaches the second-hold
        /// check, because an interleaved change is a NEW conflict the user never saw. On the
        /// hold-2 variant the orphaned derivation is best-effort revoked (twin rule 4).
        /// </summary>
        SubjectMismatch = 4,

        /// <summary>
        /// The store disappeared between the holds (a machine-wide sign-out, F6, won the race).
        /// Nothing was written — a sign-out is never answered by recreating a signed-in store.
        /// The just-derived plugin family (invisible to every other component) has been
        /// best-effort revoked when a revocation client was provided; it is also carried in
        /// <see cref="LoginCommitResult.ExchangeResult"/>. (TS twin reason: <c>store-missing</c>.)
        /// </summary>
        StoreSignedOut = 5,

        /// <summary>
        /// The store was unreadable at the second hold, so the commit's premise could not be
        /// verified. Result-shaped and RETRYABLE — nothing was written, nothing was revoked (the
        /// minted family, carried in <see cref="LoginCommitResult.ExchangeResult"/>, is what the
        /// retry commits). (TS twin reason: <c>store-unreadable</c>.)
        /// </summary>
        StoreUnreadable = 6,

        /// <summary>
        /// A CONFIRMED account switch (<c>confirmedReplaceOfSubject</c>) found the store's subject
        /// changed AGAIN since the user confirmed — the confirmation's premise is void (a third
        /// account appeared) and silently replacing it would overwrite an account the user never
        /// saw. Nothing was written, nothing revoked — the caller re-runs the F7 dialog against
        /// the store's new owner and retries with the same mint. (TS twin reason:
        /// <c>guard-premise-changed</c>, hold 1.)
        /// </summary>
        GuardPremiseChanged = 7,
    }

    /// <summary>The outcome of a login-commit sequence (see <see cref="LoginCommitStatus"/>).</summary>
    public sealed class LoginCommitResult
    {
        internal LoginCommitResult(LoginCommitStatus status, TokenExchangeResult? exchangeResult, string? detail)
        {
            Status = status;
            ExchangeResult = exchangeResult;
            Detail = detail;
        }

        /// <summary>What the sequence achieved.</summary>
        public LoginCommitStatus Status { get; }

        /// <summary>The exchange outcome (null when the exchange was never reached). Carries the minted plugin family on <see cref="LoginCommitStatus.PluginCommitBusy"/>.</summary>
        public TokenExchangeResult? ExchangeResult { get; }

        /// <summary>Human-facing detail (busy path, exchange failure reason, …). Never token material.</summary>
        public string? Detail { get; }
    }

    /// <summary>
    /// The two-lock-hold login commit sequence (unified-machine-auth 04 §4, design 03 F1 steps
    /// 3–4), exposed as ONE helper so all three engines implement F1 uniformly:
    /// <list type="number">
    ///   <item><b>First hold:</b> acquire the 04 §2 lock → re-read → subject guard (F7) → write the
    ///   freshly minted AGENT family (with the <c>clientId</c> it was minted under, D8) → release.</item>
    ///   <item><b>Exchange (no lock held):</b> RFC 8693 token exchange, agent access token →
    ///   plugin family, the exchanging component's own <c>client_id</c>.</item>
    ///   <item><b>Second hold:</b> acquire → re-read → write the PLUGIN family; the v1 compat
    ///   mirror follows automatically from the store's write contract → release.</item>
    /// </list>
    /// Two separate holds by design: a failed exchange leaves a valid, committed agent family
    /// (the F1 failure path), and no network call ever runs inside the lock's critical section
    /// beyond the §2-bounded refresh (the exchange here runs BETWEEN the holds).
    /// </summary>
    public static class MachineCredentialLoginCommit
    {
        /// <summary>
        /// Run the full sequence. <paramref name="agentFamily"/> must carry the freshly minted
        /// agent tokens AND the <c>clientId</c> they were minted under (D8 — written from the value
        /// actually presented, never inferred). <paramref name="subject"/> is the account id from
        /// the mint response (O5).
        /// <para><b>The F7 guard (twin-converged):</b> when the store's subject and
        /// <paramref name="subject"/> are BOTH known and differ (a no-sub side proceeds, F7.3),
        /// the commit refuses (<see cref="LoginCommitStatus.SubjectMismatch"/>) unless
        /// <paramref name="confirmedReplaceOfSubject"/> names the displaced account the user
        /// confirmed replacing in the F7 dialog — and the store must STILL belong to exactly that
        /// account when the hold is taken (exact-premise check inside the hold; drift ⇒
        /// <see cref="LoginCommitStatus.GuardPremiseChanged"/>, nothing written, nothing revoked).
        /// Best-effort revocation of the displaced account's families is the login surface's job
        /// (03 F7.2). <paramref name="revocationClient"/>, when provided, is used ONLY to
        /// best-effort revoke the just-derived plugin family on a terminal hold-2 abort (twin
        /// rule 4).</para>
        /// </summary>
        public static async Task<LoginCommitResult> CommitAsync(
            MachineCredentialStore store,
            MachineCredentialLock credentialLock,
            MachineCredentialFamily agentFamily,
            string? serverTarget,
            string? subject,
            ITokenExchangeClient exchangeClient,
            string? confirmedReplaceOfSubject = null,
            ITokenRevocationClient? revocationClient = null,
            ILogger? logger = null,
            CancellationToken cancellationToken = default)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (credentialLock == null) throw new ArgumentNullException(nameof(credentialLock));
            if (agentFamily == null) throw new ArgumentNullException(nameof(agentFamily));
            if (exchangeClient == null) throw new ArgumentNullException(nameof(exchangeClient));
            if (string.IsNullOrEmpty(agentFamily.AccessToken))
                throw new ArgumentException("The agent family must carry an access token (it is the exchange subject).", nameof(agentFamily));
            if (string.IsNullOrEmpty(agentFamily.ClientId))
                throw new ArgumentException("The agent family must carry the clientId it was minted under (design D8).", nameof(agentFamily));

            // ── First hold: commit the agent family. ─────────────────────────────────────────────
            var firstHold = await Task.Run(() => credentialLock.TryAcquire(), cancellationToken).ConfigureAwait(false);
            if (firstHold == null)
                return new LoginCommitResult(LoginCommitStatus.Busy, null, "credential lock busy (first hold)");

            string? premiseSubject;
            using (firstHold)
            {
                var read = store.TryRead();

                // Subject guard (F7/D6): never silently overwrite another account's machine store.
                // The compare fires only when BOTH subjects are known (a pre-a6/no-sub credential
                // proceeds, F7.3 — twin-converged). An unreadable store cannot prove a subject,
                // and login IS the explicit user re-authorization that 04 §1 requires before
                // replacing one — so Unreadable falls through to a fresh document.
                var existing = read.Status == MachineCredentialStoreStatus.Ok ? read.Credentials : null;
                if (existing?.Subject != null && subject != null
                    && !string.Equals(existing.Subject, subject, StringComparison.Ordinal))
                {
                    if (confirmedReplaceOfSubject == null)
                    {
                        return new LoginCommitResult(LoginCommitStatus.SubjectMismatch, null,
                            "the machine store belongs to a different account");
                    }
                    if (!string.Equals(existing.Subject, confirmedReplaceOfSubject, StringComparison.Ordinal))
                    {
                        // Exact-premise check INSIDE the hold (twin: guard-premise-changed): the
                        // user confirmed replacing one specific account, and the store's owner
                        // changed again since that dialog — a third account the user never saw.
                        // Nothing written, nothing revoked; the caller re-runs the F7 dialog and
                        // retries with this same mint.
                        return new LoginCommitResult(LoginCommitStatus.GuardPremiseChanged, null,
                            "the store's subject changed since the account-switch was confirmed; re-confirm against its current owner");
                    }
                    // A confirmed switch whose premise held — proceed to replace. Best-effort
                    // revocation of the displaced account's families is the login surface's job
                    // (03 F7.2), before or after this commit.
                }

                var document = existing ?? new MachineCredentials();
                var families = document.Families ?? (document.Families = new MachineCredentialFamilies());
                families.Agent = agentFamily;
                if (serverTarget != null)
                    document.ServerTarget = serverTarget;
                if (subject != null)
                    document.Subject = subject;

                if (!firstHold.IsStillOwned()) // b2 review A1: verify ownership immediately before the write
                    return new LoginCommitResult(LoginCommitStatus.Busy, null, "credential lock lost before the agent-family write");
                store.Write(document);

                // The commit's premise for the second hold (review B1): whatever subject this
                // write established is what the store must still carry when the plugin family
                // lands — an interleaved login/sign-out between the holds voids the premise.
                premiseSubject = document.Subject;
            }

            // ── Exchange, between the holds (03 F1.4). ───────────────────────────────────────────
            TokenExchangeResult exchange;
            try
            {
                exchange = await exchangeClient.ExchangeAsync(agentFamily.AccessToken!, serverTarget, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger?.LogWarning("Token exchange threw: {message}", ex.Message); // never token material
                return new LoginCommitResult(LoginCommitStatus.AgentOnly, null, ex.Message);
            }

            if (exchange == null || !exchange.Succeeded || string.IsNullOrEmpty(exchange.AccessToken))
                return new LoginCommitResult(LoginCommitStatus.AgentOnly, exchange, exchange?.FailureReason ?? "token exchange failed");

            // ── Second hold: commit the plugin family (+ mirror via the write contract). ─────────
            return await CommitPluginFamilyAsync(store, credentialLock, exchange, exchangeClient.ClientId, premiseSubject, serverTarget, revocationClient, logger, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// The second phase alone: commit an already-minted plugin family under one lock hold.
        /// Public so a <see cref="LoginCommitStatus.PluginCommitBusy"/> outcome can be retried
        /// without re-running the exchange. The family is stamped with
        /// <paramref name="presentedClientId"/> (the id actually presented at derivation, D8) and
        /// the exchange response's scope.
        /// <para><b>The F7 guard holds at THIS hold too (review B1, twin-converged):</b> the
        /// exchange ran lock-free, so before writing, the commit's premise is re-verified against
        /// the re-read store. The subject compare fires only when BOTH
        /// <paramref name="expectedSubject"/> and the store's subject are known (pre-a6/no-sub ⇒
        /// proceed, F7.3); when both are known and differ, the premise is void ⇒
        /// <see cref="LoginCommitStatus.SubjectMismatch"/> — no confirmation flag reaches this
        /// check, because an interleaved change is a conflict the user never saw. The store must
        /// still exist (a machine-wide sign-out between the holds wins ⇒
        /// <see cref="LoginCommitStatus.StoreSignedOut"/>, never recreated), and an unreadable
        /// store is a result-shaped retry (<see cref="LoginCommitStatus.StoreUnreadable"/>) —
        /// never a write, never an escaping throw. On a terminal hold-2 abort (subject-changed /
        /// store-missing) the just-derived plugin family — invisible to every other component —
        /// is best-effort revoked via <paramref name="revocationClient"/> (refresh-token
        /// preferred); the retryable unreadable outcome revokes nothing, because the retry commits
        /// that same mint. The response's <c>sub</c> is backfilled into the document
        /// only-when-absent. Because a missing store aborts, this entry point is NOT suitable for
        /// a first commit onto an empty machine (a tools-only O10 first login commits its plugin
        /// family the way <see cref="CommitAsync"/>'s first hold commits the agent family).</para>
        /// </summary>
        public static async Task<LoginCommitResult> CommitPluginFamilyAsync(
            MachineCredentialStore store,
            MachineCredentialLock credentialLock,
            TokenExchangeResult exchange,
            string presentedClientId,
            string? expectedSubject,
            string? serverTarget = null,
            ITokenRevocationClient? revocationClient = null,
            ILogger? logger = null,
            CancellationToken cancellationToken = default)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (credentialLock == null) throw new ArgumentNullException(nameof(credentialLock));
            if (exchange == null) throw new ArgumentNullException(nameof(exchange));
            if (!exchange.Succeeded || string.IsNullOrEmpty(exchange.AccessToken))
                throw new ArgumentException("Only a successful exchange result can be committed.", nameof(exchange));
            if (string.IsNullOrEmpty(presentedClientId))
                throw new ArgumentException("The clientId actually presented at derivation is required (design D8).", nameof(presentedClientId));

            var hold = await Task.Run(() => credentialLock.TryAcquire(), cancellationToken).ConfigureAwait(false);
            if (hold == null)
                return new LoginCommitResult(LoginCommitStatus.PluginCommitBusy, exchange, "credential lock busy (second hold)");

            // Re-verify the commit's premise under the second hold (review B1, 03 F7.2): the
            // exchange ran lock-free, so an interleaved login or a machine-wide sign-out may have
            // voided it. A plugin family derived from account A must never land in a store that is
            // no longer A's.
            LoginCommitResult result;
            using (hold)
            {
                result = CommitPluginFamilyUnderHold(store, hold, exchange, presentedClientId, expectedSubject, serverTarget, logger);
            }

            // Orphan revocation OUTSIDE the critical section (04 §2: the hold stays short) and only
            // for the TERMINAL hold-2 aborts — the just-derived family is invisible to every other
            // component, so nobody else can revoke or use it. Retryable outcomes (busy/unreadable)
            // revoke nothing: the retry commits this same mint. Hold-1 premise aborts never reach
            // this method at all, so they revoke nothing either (twin rule 4).
            if (result.Status == LoginCommitStatus.StoreSignedOut || result.Status == LoginCommitStatus.SubjectMismatch)
                await RevokeOrphanedFamilyAsync(revocationClient, exchange, presentedClientId, serverTarget, logger, cancellationToken).ConfigureAwait(false);

            return result;
        }

        // The hold-2 critical section (store I/O only — no network). MUST be called with the hold held.
        static LoginCommitResult CommitPluginFamilyUnderHold(
            MachineCredentialStore store,
            MachineCredentialLockHandle hold,
            TokenExchangeResult exchange,
            string presentedClientId,
            string? expectedSubject,
            string? serverTarget,
            ILogger? logger)
        {
            var read = store.TryRead();
            if (read.Status == MachineCredentialStoreStatus.Unreadable)
            {
                // The premise cannot be verified against a store that cannot be read: a
                // result-shaped, RETRYABLE outcome — never a write, never an escaping throw,
                // and NO revocation (the retry commits this same mint). Twin: store-unreadable.
                logger?.LogWarning("Plugin-family commit deferred: store unreadable at the second hold ({error}).", read.Error);
                return new LoginCommitResult(LoginCommitStatus.StoreUnreadable, exchange,
                    "credential store unreadable at the second hold; retry the plugin-family commit");
            }
            if (read.Status == MachineCredentialStoreStatus.Missing || read.Credentials == null)
            {
                // A machine-wide sign-out (F6) landed between the holds — the sign-out wins.
                // Never answer it by recreating a signed-in store. Twin: store-missing.
                logger?.LogWarning("Plugin-family commit aborted: the store was signed out between the holds.");
                return new LoginCommitResult(LoginCommitStatus.StoreSignedOut, exchange,
                    "the machine was signed out between the holds; the plugin family was not committed");
            }

            var document = read.Credentials;
            if (document.Subject != null && expectedSubject != null
                && !string.Equals(document.Subject, expectedSubject, StringComparison.Ordinal))
            {
                // Interleaved login (03 F7.2): the store's subject is no longer the commit's
                // premise. Fires only when BOTH subjects are known (pre-a6/no-sub proceeds,
                // F7.3 — twin-converged); no confirmation flag reaches here, because this
                // conflict postdates the user's answer. Twin: subject-changed.
                logger?.LogWarning("Plugin-family commit aborted: the store's subject changed between the holds.");
                return new LoginCommitResult(LoginCommitStatus.SubjectMismatch, exchange,
                    "the store's subject changed between the holds (interleaved login); the plugin family was not committed");
            }

            var families = document.Families ?? (document.Families = new MachineCredentialFamilies());

            families.Plugin = new MachineCredentialFamily
            {
                AccessToken = exchange.AccessToken,
                RefreshToken = exchange.RefreshToken,
                ExpiresAt = exchange.ExpiresAt,
                ClientId = presentedClientId,
                Scope = exchange.Scope ?? TokenExchangeClient.PluginScope,
            };
            if (serverTarget != null && document.ServerTarget == null)
                document.ServerTarget = serverTarget;
            if (exchange.Subject != null && document.Subject == null)
                document.Subject = exchange.Subject; // O5 sub backfill — only-when-absent (twin rule 5)

            if (!hold.IsStillOwned()) // b2 review A1
                return new LoginCommitResult(LoginCommitStatus.PluginCommitBusy, exchange, "credential lock lost before the plugin-family write");
            store.Write(document); // the v1 compat mirror of families.plugin is stamped by the write contract

            logger?.LogInformation("Login commit complete: agent + plugin families persisted to the machine store.");
            return new LoginCommitResult(LoginCommitStatus.FullyCommitted, exchange, null);
        }

        /// <summary>
        /// Best-effort RFC 7009 revocation of a derived-but-never-committed plugin family
        /// (twin rule 4): refresh token preferred (revoking it kills the family), falling back to
        /// the access token; single attempt, failures only logged — the family also dies naturally
        /// at its own expiry, and the abort result still carries it for the caller.
        /// </summary>
        static async Task RevokeOrphanedFamilyAsync(
            ITokenRevocationClient? revocationClient,
            TokenExchangeResult exchange,
            string presentedClientId,
            string? serverTarget,
            ILogger? logger,
            CancellationToken cancellationToken)
        {
            if (revocationClient == null)
                return;

            var token = !string.IsNullOrEmpty(exchange.RefreshToken) ? exchange.RefreshToken : exchange.AccessToken;
            if (string.IsNullOrEmpty(token))
                return;

            try
            {
                var acknowledged = await revocationClient.RevokeAsync(token!, presentedClientId, serverTarget, cancellationToken).ConfigureAwait(false);
                if (!acknowledged)
                    logger?.LogWarning("Best-effort revocation of the orphaned plugin family was not acknowledged; it expires naturally.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger?.LogWarning("Best-effort revocation of the orphaned plugin family threw: {message}", ex.Message); // never token material
            }
        }
    }
}
