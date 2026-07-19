/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
└────────────────────────────────────────────────────────────────────────┘
*/

#nullable enable
using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Auth.OAuth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Tests.OAuth;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// End-to-end regression for the B11 root cause (auth-fixes, k1 code-trace 2026-07-18): a plugin
    /// hub-token (<c>aud=urn:agd:hub</c>) is validated by the REAL <see cref="AccessTokenValidator"/> on
    /// the plugin plane and drives an instance registration into <see cref="AccountMcpStrategy"/> exactly
    /// as <c>McpServerHub.TryRegisterOAuthInstanceAsync</c> does — closing the test blind spot noted in
    /// 01 §7 (the prior handshake tests build <see cref="ConnectionIdentity"/> directly and never drove
    /// the aud validator with a real <c>urn:agd:hub</c> token). Before the fix the strict agent-plane
    /// aud check rejected the token → the bucket stayed empty → the agent's <c>tools/list</c> silently
    /// degraded to the 3 native tools (B6). Also covers plane separation (an agent token cannot register)
    /// and the late-connect <c>list_changed</c> delivery (B6) that registration restores.
    /// </summary>
    public sealed class B11PluginPlaneRegistrationTests
    {
        const string Issuer = "https://as.example";
        const string Resource = "http://localhost:23471";
        const string Kid = "key-1";
        const string Account = "user-abc"; // the plugin token's `sub` == the agent's account
        const string PluginConnectionId = "plugin-conn-1";
        const string ProjectPathHash = "abcd1234ef567890abcd1234ef567890abcd1234ef567890abcd1234ef567890";
        const string Pin = "abcd1234";
        static readonly DateTimeOffset Now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        static AccessTokenValidator Validator(ECDsa key)
        {
            var jwks = new FakeJwksKeyProvider().Add(Kid, key);
            return new AccessTokenValidator(new OAuthResourceServerConfig(Issuer, Resource), jwks, FakeIntrospectionClient.AlwaysInactive, () => Now);
        }

        static string PluginHubToken(ECDsa key)
            => TestJwt.SignEs256(key, Kid, TestJwt.Claims(Issuer, AccessTokenValidator.PluginAudience, Now.AddHours(1), sub: Account, scope: ConnectionIdentity.ScopePlugin));

        static string AgentToken(ECDsa key)
            => TestJwt.SignEs256(key, Kid, TestJwt.Claims(Issuer, Resource, Now.AddHours(1), sub: Account, scope: ConnectionIdentity.ScopeAgent));

        static PluginInstanceMetadata Metadata()
            => AccountMcpStrategy.BuildInstanceMetadata(
                connectionId: PluginConnectionId,
                instanceId: "sess-42",
                engine: "unity",
                projectName: "MyGame",
                projectPathHash: ProjectPathHash,
                machineName: "DESKTOP-9");

        [Fact]
        public async Task ValidatedPluginHubToken_RegistersInstance_AndAgentSeesIt()
        {
            using var key = TestJwt.CreateKey();
            var strategy = new AccountMcpStrategy();

            // Before: the agent's account bucket is empty → tools/list = AccountEmpty (the B11 symptom).
            strategy.Instances.Resolve(Account, Pin, selectedInstanceId: null)
                .Kind.ShouldBe(InstanceResolutionKind.AccountEmpty);

            // Drive the REAL validator on the plugin plane exactly as McpServerHub does.
            var validation = await Validator(key).ValidateAsync(PluginHubToken(key), TokenValidationPlane.Plugin, CancellationToken.None);
            validation.Succeeded.ShouldBeTrue(validation.FailureReason);

            var identity = ConnectionIdentity.Create(validation.Subject, validation.Scope, validation.ClientId);
            identity.ShouldNotBeNull();
            identity!.AccountId.ShouldBe(Account);
            identity.IsPlugin.ShouldBeTrue();

            strategy.RegisterInstance(identity, Metadata(), PluginConnectionId, NullLogger.Instance);

            // After: the agent (same sub) now resolves the live instance by its project pin — full toolset.
            var resolution = strategy.Instances.Resolve(Account, Pin, selectedInstanceId: null);
            resolution.Kind.ShouldBe(InstanceResolutionKind.Resolved);
            resolution.Instance!.InstanceId.ShouldBe("sess-42");
        }

        [Fact]
        public async Task AgentToken_OnPluginPlane_IsRejected_SoItNeverRegisters()
        {
            // Plane separation: an agent token validated on the plugin plane fails, so
            // McpServerHub gets identity==null and never registers a plugin instance for it.
            using var key = TestJwt.CreateKey();

            var validation = await Validator(key).ValidateAsync(AgentToken(key), TokenValidationPlane.Plugin, CancellationToken.None);

            validation.Succeeded.ShouldBeFalse();
            ConnectionIdentity.Create(validation.Subject, validation.Scope, validation.ClientId).ShouldBeNull();
        }

        [Fact]
        public async Task LateConnect_AfterRegistration_NotifiesTheLiveAgentSession()
        {
            // B6 late-connect: an editor connecting AFTER the agent's first tools/list must deliver
            // notifications/tools/list_changed to the live session. That delivery is gated by
            // ShouldNotifySession → GetAccountByConnection(pluginConnectionId), which is null for an
            // unregistered plugin. Fixing registration (B11) restores it.
            using var key = TestJwt.CreateKey();
            var strategy = new AccountMcpStrategy();

            // Agent session (sessionId == account in oauth) is already live; the plugin has NOT registered.
            strategy.ShouldNotifySession(PluginConnectionId, sessionId: Account).ShouldBeFalse();

            var validation = await Validator(key).ValidateAsync(PluginHubToken(key), TokenValidationPlane.Plugin, CancellationToken.None);
            var identity = ConnectionIdentity.Create(validation.Subject, validation.Scope, validation.ClientId)!;
            strategy.RegisterInstance(identity, Metadata(), PluginConnectionId, NullLogger.Instance);

            // Now the late-connect list_changed reaches the live agent session — and no other account's.
            strategy.ShouldNotifySession(PluginConnectionId, sessionId: Account).ShouldBeTrue();
            strategy.ShouldNotifySession(PluginConnectionId, sessionId: "someone-else").ShouldBeFalse();
        }
    }
}
