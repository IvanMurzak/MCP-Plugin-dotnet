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
        public async Task Commit_DifferentSubject_WithExplicitOverwrite_Proceeds()
        {
            NewStore().Write(new MachineCredentials { Subject = "usr_OTHER" });
            var exchange = new FakeExchangeClient(SuccessfulExchange());

            var result = await MachineCredentialLoginCommit.CommitAsync(
                NewStore(), NewLock(), NewAgentFamily(), ServerTarget, "usr_123", exchange,
                overwriteDifferentSubject: true);

            result.Status.ShouldBe(LoginCommitStatus.FullyCommitted);
            NewStore().Read()!.Subject.ShouldBe("usr_123");
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
                NewStore(), NewLock(), SuccessfulExchange(), OwnClientId, ServerTarget);

            result.Status.ShouldBe(LoginCommitStatus.FullyCommitted);
            var stored = NewStore().Read()!;
            stored.Families!.Agent.ShouldNotBeNull(); // preserved
            stored.Families.Plugin!.AccessToken.ShouldBe("eyJ.PLUGIN.sig");
            stored.AccessToken.ShouldBe("eyJ.PLUGIN.sig"); // mirror
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
