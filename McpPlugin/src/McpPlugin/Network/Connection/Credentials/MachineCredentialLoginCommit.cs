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
        /// The store already belongs to a different account (<c>subject</c> mismatch — design F7/D6)
        /// and the caller did not pass <c>overwriteDifferentSubject</c>. Nothing was written; the
        /// caller shows the account-switch confirm dialog and retries with the flag (after
        /// best-effort revoking the displaced account's families) or aborts (revoking the
        /// just-minted tokens).
        /// </summary>
        SubjectMismatch = 4,
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
        /// the mint response (O5). <paramref name="overwriteDifferentSubject"/> false (default)
        /// refuses to overwrite a store belonging to a different account
        /// (<see cref="LoginCommitStatus.SubjectMismatch"/>) — the F7 guard.
        /// </summary>
        public static async Task<LoginCommitResult> CommitAsync(
            MachineCredentialStore store,
            MachineCredentialLock credentialLock,
            MachineCredentialFamily agentFamily,
            string? serverTarget,
            string? subject,
            ITokenExchangeClient exchangeClient,
            bool overwriteDifferentSubject = false,
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

            using (firstHold)
            {
                var read = store.TryRead();

                // Subject guard (F7/D6): never silently overwrite another account's machine store.
                // An unreadable store cannot prove a subject, and login IS the explicit user
                // re-authorization that 04 §1 requires before replacing one — so Unreadable falls
                // through to a fresh document.
                var existing = read.Status == MachineCredentialStoreStatus.Ok ? read.Credentials : null;
                if (!overwriteDifferentSubject
                    && existing?.Subject != null && subject != null
                    && !string.Equals(existing.Subject, subject, StringComparison.Ordinal))
                {
                    return new LoginCommitResult(LoginCommitStatus.SubjectMismatch, null,
                        "the machine store belongs to a different account");
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
            return await CommitPluginFamilyAsync(store, credentialLock, exchange, exchangeClient.ClientId, serverTarget, logger, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// The second phase alone: commit an already-minted plugin family under one lock hold.
        /// Public so a <see cref="LoginCommitStatus.PluginCommitBusy"/> outcome can be retried
        /// without re-running the exchange, and so a tools-only (O10) login — which mints a plugin
        /// family directly — commits through the same path. The family is stamped with
        /// <paramref name="presentedClientId"/> (the id actually presented at derivation, D8) and
        /// the exchange response's scope.
        /// </summary>
        public static async Task<LoginCommitResult> CommitPluginFamilyAsync(
            MachineCredentialStore store,
            MachineCredentialLock credentialLock,
            TokenExchangeResult exchange,
            string presentedClientId,
            string? serverTarget = null,
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

            using (hold)
            {
                var read = store.TryRead();
                var document = read.Status == MachineCredentialStoreStatus.Ok && read.Credentials != null
                    ? read.Credentials
                    : new MachineCredentials(); // Missing/Unreadable mid-login: this login is the explicit re-auth (04 §1)
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
                    document.Subject = exchange.Subject; // O5: no path writes a subject-less credential

                if (!hold.IsStillOwned()) // b2 review A1
                    return new LoginCommitResult(LoginCommitStatus.PluginCommitBusy, exchange, "credential lock lost before the plugin-family write");
                store.Write(document); // the v1 compat mirror of families.plugin is stamped by the write contract

                logger?.LogInformation("Login commit complete: agent + plugin families persisted to the machine store.");
                return new LoginCommitResult(LoginCommitStatus.FullyCommitted, exchange, null);
            }
        }
    }
}
