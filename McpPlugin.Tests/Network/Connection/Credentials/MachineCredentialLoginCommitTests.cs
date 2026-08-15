/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.AgentConfig;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection.Credentials
{
    /// <summary>
    /// The two-lock-hold login commit sequence (unified-machine-auth 04 §4, design 03 F1 steps
    /// 3–4): agent family under the first hold, RFC 8693 exchange between the holds, plugin family
    /// + v1 mirror under the second — and the failure paths that motivate the two separate holds.
    /// </summary>
    public sealed class MachineCredentialLoginCommitTests : IDisposable
    {
        const string OwnClientId = "unreal-mcp-plugin";
        const string ServerTarget = "https://ai-game.dev";

        readonly string _baseDir;

        public MachineCredentialLoginCommitTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "agd-logincommit-" + Guid.NewGuid().ToString("N"), ".ai-game-dev");
        }

        public void Dispose()
        {
            var parent = Path.GetDirectoryName(_baseDir);
            if (parent != null && Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }

        MachineCredentialStore NewStore() => new MachineCredentialStore(_baseDir);
        MachineCredentialLock NewLock() => new MachineCredentialLock(_baseDir);

        static MachineCredentialFamily NewAgentFamily() => new MachineCredentialFamily
        {
            AccessToken = "eyJ.AGENT.sig",
            RefreshToken = "RT-agent-aaa",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            ClientId = OwnClientId,
            Scope = "mcp:agent",
        };

        sealed class FakeExchangeClient : ITokenExchangeClient
        {
            readonly Func<string, TokenExchangeResult> _onExchange;
            public readonly List<string> SubjectTokens = new List<string>();

            public FakeExchangeClient(Func<string, TokenExchangeResult> onExchange) { _onExchange = onExchange; }
            public FakeExchangeClient(TokenExchangeResult fixedResult) : this(_ => fixedResult) { }

            public string ClientId => OwnClientId;

            public Task<TokenExchangeResult> ExchangeAsync(string subjectAccessToken, string? serverTarget, CancellationToken cancellationToken = default)
            {
                SubjectTokens.Add(subjectAccessToken);
                return Task.FromResult(_onExchange(subjectAccessToken));
            }
        }

        static TokenExchangeResult SuccessfulExchange() => TokenExchangeResult.Success(
            "eyJ.PLUGIN.sig", "RT-plugin-aaa", DateTimeOffset.UtcNow.AddHours(1), "mcp:plugin", "usr_123",
            "urn:ietf:params:oauth:token-type:access_token");

        sealed class FakeRevocationClient : ITokenRevocationClient
        {
            public readonly List<(string Token, string? ClientId)> Calls = new List<(string, string?)>();

            public Task<bool> RevokeAsync(string token, string? clientId, string? serverTarget, CancellationToken cancellationToken = default)
            {
                Calls.Add((token, clientId));
                return Task.FromResult(true);
            }
        }

        // ── The happy path: F1 steps 3–5. ──

        [Fact]
        public async Task Commit_FullSequence_PersistsAgentThenPluginFamilies_WithMirror()
        {
            var exchange = new FakeExchangeClient(SuccessfulExchange());

            var result = await MachineCredentialLoginCommit.CommitAsync(
                NewStore(), NewLock(), NewAgentFamily(), ServerTarget, "usr_123", exchange);

            result.Status.ShouldBe(LoginCommitStatus.FullyCommitted);

            // The exchange subject was the agent access token (04 §4: a fresh ES256 agent token).
            exchange.SubjectTokens.ShouldHaveSingleItem().ShouldBe("eyJ.AGENT.sig");

            var stored = NewStore().Read()!;
            stored.Subject.ShouldBe("usr_123");
            stored.ServerTarget.ShouldBe(ServerTarget);
            // Agent family, with the clientId it was minted under (D8).
            stored.Families!.Agent!.AccessToken.ShouldBe("eyJ.AGENT.sig");
            stored.Families.Agent.ClientId.ShouldBe(OwnClientId);
            // Plugin family, stamped with the id the exchange PRESENTED and the response scope.
            stored.Families.Plugin!.AccessToken.ShouldBe("eyJ.PLUGIN.sig");
            stored.Families.Plugin.RefreshToken.ShouldBe("RT-plugin-aaa");
            stored.Families.Plugin.ClientId.ShouldBe(OwnClientId);
            stored.Families.Plugin.Scope.ShouldBe("mcp:plugin");
            // The v1 compat mirror follows the plugin family (write contract).
            stored.AccessToken.ShouldBe("eyJ.PLUGIN.sig");
            stored.RefreshToken.ShouldBe("RT-plugin-aaa");

            // Both holds were released (no residual lock artifact).
            File.Exists(NewLock().LockPath).ShouldBeFalse();
        }

        // ── The F1 failure path: a failed exchange leaves a committed agent family. ──

        [Fact]
        public async Task Commit_ExchangeFails_AgentFamilyStaysCommitted_StatusAgentOnly()
        {
            var exchange = new FakeExchangeClient(TokenExchangeResult.Failure("as unreachable"));

            var result = await MachineCredentialLoginCommit.CommitAsync(
                NewStore(), NewLock(), NewAgentFamily(), ServerTarget, "usr_123", exchange);

            result.Status.ShouldBe(LoginCommitStatus.AgentOnly); // caller retries with backoff (F1)
            var stored = NewStore().Read()!;
            stored.Families!.Agent!.AccessToken.ShouldBe("eyJ.AGENT.sig"); // first hold committed
            stored.Families.Plugin.ShouldBeNull();
            stored.AccessToken.ShouldBeNull(); // no plugin-plane credential ⇒ no v1 mirror
        }

        [Fact]
        public async Task Commit_ExchangeThrows_AgentFamilyStaysCommitted_StatusAgentOnly()
        {
            var exchange = new FakeExchangeClient(_ => throw new InvalidOperationException("boom"));

            var result = await MachineCredentialLoginCommit.CommitAsync(
                NewStore(), NewLock(), NewAgentFamily(), ServerTarget, "usr_123", exchange);

            result.Status.ShouldBe(LoginCommitStatus.AgentOnly);
            NewStore().Read()!.Families!.Agent.ShouldNotBeNull();
        }

        // ── The subject guard (F7/D6): never silently overwrite another account's store. ──

        [Fact]
        public async Task Commit_DifferentSubjectInStore_RefusesWithoutWriting()
        {
            NewStore().Write(new MachineCredentials
            {
                Subject = "usr_OTHER",
                Families = new MachineCredentialFamilies
                {
                    Plugin = new MachineCredentialFamily { AccessToken = "eyJ.OTHER.sig", RefreshToken = "RT-other" },
                },
            });
            var exchange = new FakeExchangeClient(SuccessfulExchange());

            var result = await MachineCredentialLoginCommit.CommitAsync(
                NewStore(), NewLock(), NewAgentFamily(), ServerTarget, "usr_123", exchange);

            result.Status.ShouldBe(LoginCommitStatus.SubjectMismatch);
            exchange.SubjectTokens.ShouldBeEmpty(); // refused before any network call
            var stored = NewStore().Read()!;
            stored.Subject.ShouldBe("usr_OTHER"); // untouched
            stored.Families!.Agent.ShouldBeNull();
        }

        [Fact]
        public async Task Commit_DifferentSubject_WithConfirmedSwitch_Proceeds()
        {
            NewStore().Write(new MachineCredentials { Subject = "usr_OTHER" });
            var exchange = new FakeExchangeClient(SuccessfulExchange());

            // The user confirmed replacing usr_OTHER in the F7 dialog — the confirmation names
            // exactly the account being displaced (twin: exact-premise confirmed switch).
            var result = await MachineCredentialLoginCommit.CommitAsync(
                NewStore(), NewLock(), NewAgentFamily(), ServerTarget, "usr_123", exchange,
                confirmedReplaceOfSubject: "usr_OTHER");

            result.Status.ShouldBe(LoginCommitStatus.FullyCommitted);
            NewStore().Read()!.Subject.ShouldBe("usr_123");
        }

        [Fact]
        public async Task Commit_ConfirmedSwitch_ButStoreOwnerChangedAgain_AbortsGuardPremiseChanged()
        {
            // The user confirmed replacing usr_OTHER — but by the time the hold is taken, a THIRD
            // account owns the store. The confirmation's premise is void (twin:
            // guard-premise-changed): nothing written, nothing exchanged, nothing revoked.
            NewStore().Write(new MachineCredentials { Subject = "usr_THIRD" });
            var exchange = new FakeExchangeClient(SuccessfulExchange());
            var revocation = new FakeRevocationClient();

            var result = await MachineCredentialLoginCommit.CommitAsync(
                NewStore(), NewLock(), NewAgentFamily(), ServerTarget, "usr_123", exchange,
                confirmedReplaceOfSubject: "usr_OTHER", revocationClient: revocation);

            result.Status.ShouldBe(LoginCommitStatus.GuardPremiseChanged);
            exchange.SubjectTokens.ShouldBeEmpty();          // refused before any network call
            revocation.Calls.ShouldBeEmpty();                // hold-1 premise aborts revoke NOTHING (twin rule 4)
            var stored = NewStore().Read()!;
            stored.Subject.ShouldBe("usr_THIRD");            // untouched
            stored.Families?.Agent.ShouldBeNull();
        }

        // ── Busy paths: nothing written outside the lock. ──

        [Fact]
        public async Task Commit_FirstHoldBusy_WritesNothing()
        {
            var exchange = new FakeExchangeClient(SuccessfulExchange());
            var shortBudgetLock = new MachineCredentialLock(_baseDir, hostId: null,
                acquireBudgetMs: 200, staleMs: MachineCredentialLock.LOCK_STALE_MS,
                foreignStaleMs: MachineCredentialLock.FOREIGN_HOST_STALE_MS);

            using var peerHold = NewLock().TryAcquire();
            peerHold.ShouldNotBeNull();

            var result = await MachineCredentialLoginCommit.CommitAsync(
                NewStore(), shortBudgetLock, NewAgentFamily(), ServerTarget, "usr_123", exchange);

            result.Status.ShouldBe(LoginCommitStatus.Busy);
            exchange.SubjectTokens.ShouldBeEmpty();
            NewStore().Exists.ShouldBeFalse(); // nothing was written lock-free
        }

        [Fact]
        public async Task CommitPluginFamily_RetriesTheSecondPhaseAlone_WithoutReExchanging()
        {
            // Simulate a PluginCommitBusy recovery: the agent family is already committed and the
            // exchange result is in hand — only the second phase runs.
            NewStore().Write(new MachineCredentials
            {
                Subject = "usr_123",
                ServerTarget = ServerTarget,
                Families = new MachineCredentialFamilies { Agent = NewAgentFamily() },
            });

            var result = await MachineCredentialLoginCommit.CommitPluginFamilyAsync(
                NewStore(), NewLock(), SuccessfulExchange(), OwnClientId, expectedSubject: "usr_123", ServerTarget);

            result.Status.ShouldBe(LoginCommitStatus.FullyCommitted);
            var stored = NewStore().Read()!;
            stored.Families!.Agent.ShouldNotBeNull(); // preserved
            stored.Families.Plugin!.AccessToken.ShouldBe("eyJ.PLUGIN.sig");
            stored.AccessToken.ShouldBe("eyJ.PLUGIN.sig"); // mirror
        }

        // ── The F7 guard at the SECOND hold (review B1): the commit's premise is re-verified
        //    before the plugin-family write — an interleaved login/sign-out between the holds
        //    must abort, never mix accounts, never recreate a signed-out store. ──

        [Fact]
        public async Task Commit_SubjectChangedBetweenHolds_AbortsSecondPhase_NeverMixesAccounts_RevokesTheOrphan()
        {
            var exchange = new FakeExchangeClient(_ =>
            {
                // Interleaved login-as-B lands BETWEEN the holds (the exchange runs lock-free):
                // the store now belongs to account B while our exchange derived from account A.
                NewStore().Write(new MachineCredentials
                {
                    Subject = "usr_B",
                    ServerTarget = ServerTarget,
                    Families = new MachineCredentialFamilies
                    {
                        Agent = new MachineCredentialFamily
                        {
                            AccessToken = "eyJ.B-AGENT.sig",
                            RefreshToken = "RT-b-agent",
                            ClientId = "app-dcr-b",
                            Scope = "mcp:agent",
                        },
                    },
                });
                return SuccessfulExchange();
            });
            var revocation = new FakeRevocationClient();

            var result = await MachineCredentialLoginCommit.CommitAsync(
                NewStore(), NewLock(), NewAgentFamily(), ServerTarget, "usr_123", exchange,
                revocationClient: revocation);

            // 03 F7.2: a store with subject B plus a plugin family derived from account A is the
            // forbidden silent cross-account mix — the second phase must abort without writing.
            result.Status.ShouldBe(LoginCommitStatus.SubjectMismatch);
            var stored = NewStore().Read()!;
            stored.Subject.ShouldBe("usr_B");                    // B's store is untouched
            stored.Families!.Plugin.ShouldBeNull();              // A's plugin family was NOT written
            stored.Families.Agent!.AccessToken.ShouldBe("eyJ.B-AGENT.sig");

            // Twin rule 4: the just-derived, invisible-to-others plugin family is best-effort
            // revoked — refresh-token preferred, presented under the deriving client's id.
            var revoked = revocation.Calls.ShouldHaveSingleItem();
            revoked.Token.ShouldBe("RT-plugin-aaa");
            revoked.ClientId.ShouldBe(OwnClientId);
        }

        [Fact]
        public async Task Commit_StoreDeletedBetweenHolds_Aborts_NeverRecreates_RevokesTheOrphan()
        {
            var exchange = new FakeExchangeClient(_ =>
            {
                NewStore().Delete(); // a machine-wide sign-out (F6) lands between the holds
                return SuccessfulExchange();
            });
            var revocation = new FakeRevocationClient();

            var result = await MachineCredentialLoginCommit.CommitAsync(
                NewStore(), NewLock(), NewAgentFamily(), ServerTarget, "usr_123", exchange,
                revocationClient: revocation);

            result.Status.ShouldBe(LoginCommitStatus.StoreSignedOut);
            NewStore().Exists.ShouldBeFalse(); // the sign-out wins — never recreated signed-in
            revocation.Calls.ShouldHaveSingleItem().Token.ShouldBe("RT-plugin-aaa"); // orphan revoked
        }

        [Fact]
        public async Task Commit_HoldOneSubjectMismatch_RevokesNothing()
        {
            // Hold-1 premise aborts revoke NOTHING (twin rule 4): no plugin family was derived
            // yet, and the agent mint is reused by the retry after the F7 dialog.
            NewStore().Write(new MachineCredentials { Subject = "usr_OTHER" });
            var exchange = new FakeExchangeClient(SuccessfulExchange());
            var revocation = new FakeRevocationClient();

            var result = await MachineCredentialLoginCommit.CommitAsync(
                NewStore(), NewLock(), NewAgentFamily(), ServerTarget, "usr_123", exchange,
                revocationClient: revocation);

            result.Status.ShouldBe(LoginCommitStatus.SubjectMismatch);
            revocation.Calls.ShouldBeEmpty();
            exchange.SubjectTokens.ShouldBeEmpty();
        }

        [Fact]
        public async Task CommitPluginFamily_SubjectMismatchAtTheSecondHold_AbortsWithoutWriting()
        {
            NewStore().Write(new MachineCredentials { Subject = "usr_B" });

            var result = await MachineCredentialLoginCommit.CommitPluginFamilyAsync(
                NewStore(), NewLock(), SuccessfulExchange(), OwnClientId, expectedSubject: "usr_123", ServerTarget);

            result.Status.ShouldBe(LoginCommitStatus.SubjectMismatch);
            var stored = NewStore().Read()!;
            stored.Subject.ShouldBe("usr_B");
            stored.Families?.Plugin.ShouldBeNull();
        }

        [Fact]
        public async Task CommitPluginFamily_StoreMissing_Aborts_NeverRecreates()
        {
            var result = await MachineCredentialLoginCommit.CommitPluginFamilyAsync(
                NewStore(), NewLock(), SuccessfulExchange(), OwnClientId, expectedSubject: "usr_123", ServerTarget);

            result.Status.ShouldBe(LoginCommitStatus.StoreSignedOut);
            NewStore().Exists.ShouldBeFalse();
        }

        [Fact]
        public async Task CommitPluginFamily_UnreadableStore_ReturnsARetryShapedOutcome_NeverWrites_NeverRevokes()
        {
            Directory.CreateDirectory(_baseDir);
            var credentialsPath = Path.Combine(_baseDir, MachineCredentialStore.CredentialsFileName);
            File.WriteAllBytes(credentialsPath, new byte[] { 0x01, 0x02, 0x03 });
            var corrupted = File.ReadAllBytes(credentialsPath);
            var revocation = new FakeRevocationClient();

            var result = await MachineCredentialLoginCommit.CommitPluginFamilyAsync(
                NewStore(), NewLock(), SuccessfulExchange(), OwnClientId, expectedSubject: "usr_123", ServerTarget,
                revocationClient: revocation);

            // Result-shaped (no exception), retryable, the unreadable file untouched — and the
            // mint NOT revoked, because the retry commits this same mint (twin: store-unreadable).
            result.Status.ShouldBe(LoginCommitStatus.StoreUnreadable);
            result.ExchangeResult.ShouldNotBeNull(); // the minted family is carried for the retry
            File.ReadAllBytes(credentialsPath).ShouldBe(corrupted);
            revocation.Calls.ShouldBeEmpty();
        }

        [Fact]
        public async Task Commit_SubjectlessPremise_InterleavedSubjectfulLogin_ProceedsPerF73()
        {
            // Twin-converged (F7.3): subject compares fire only when BOTH subjects are known. A
            // subject-less premise (pre-a6/no-sub mint) cannot discriminate ownership, so the
            // commit proceeds — and the sub backfill is only-when-absent, so the interloper's
            // stamped subject is NOT overwritten.
            var exchange = new FakeExchangeClient(_ =>
            {
                var doc = NewStore().Read()!;
                doc.Subject = "usr_B";
                NewStore().Write(doc);
                return SuccessfulExchange();
            });

            var result = await MachineCredentialLoginCommit.CommitAsync(
                NewStore(), NewLock(), NewAgentFamily(), ServerTarget, subject: null, exchange);

            result.Status.ShouldBe(LoginCommitStatus.FullyCommitted);
            var stored = NewStore().Read()!;
            stored.Subject.ShouldBe("usr_B");                       // backfill only-when-absent (twin rule 5)
            stored.Families!.Plugin!.AccessToken.ShouldBe("eyJ.PLUGIN.sig");
        }

        [Fact]
        public async Task CommitPluginFamily_SubBackfill_OnlyWhenAbsent()
        {
            // A subject-less store + a successful exchange carrying sub ⇒ the sub is backfilled
            // (O5); a store that already carries a subject keeps it (previous test's negative).
            NewStore().Write(new MachineCredentials
            {
                ServerTarget = ServerTarget,
                Families = new MachineCredentialFamilies { Agent = NewAgentFamily() },
            });

            var result = await MachineCredentialLoginCommit.CommitPluginFamilyAsync(
                NewStore(), NewLock(), SuccessfulExchange(), OwnClientId, expectedSubject: null, ServerTarget);

            result.Status.ShouldBe(LoginCommitStatus.FullyCommitted);
            NewStore().Read()!.Subject.ShouldBe("usr_123"); // from the exchange response's sub
        }

        [Fact]
        public async Task Commit_RequiresTheAgentFamilysMintClientId()
        {
            var family = NewAgentFamily();
            family.ClientId = null; // D8 violation: an agent family must know its mint id

            await Should.ThrowAsync<ArgumentException>(() => MachineCredentialLoginCommit.CommitAsync(
                NewStore(), NewLock(), family, ServerTarget, "usr_123",
                new FakeExchangeClient(SuccessfulExchange())));
        }
    }
}
