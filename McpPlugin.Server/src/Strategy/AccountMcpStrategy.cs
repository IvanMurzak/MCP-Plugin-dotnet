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
using System.Linq;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server.Strategy
{
    /// <summary>
    /// The <c>oauth</c> account+instance pairing plane (mcp-authorize b3, design doc 04). Replaces the
    /// interim token-equality routing (<c>RequiredAuthMcpStrategy</c>, still used by legacy
    /// <c>required</c> mode until b5) with account-scoped instance routing:
    /// <list type="bullet">
    ///   <item><b>Auth config</b> — flags the OAuth resource-server validation path (same as b2's
    ///   interim <c>OAuthMcpStrategy</c>, which this class supersedes).</item>
    ///   <item><b>Routing</b> — an agent session resolves to a live <see cref="PluginInstance"/> via
    ///   <c>pin(strict) → sticky → single → MRU</c> against the account's registry bucket
    ///   (<see cref="Instances"/>).</item>
    ///   <item><b>Notifications / data</b> — strictly account-scoped; a session for one account can
    ///   never route to, be notified about, or observe another account's instances (fail closed).</item>
    /// </list>
    /// Plugin identity validation + instance registration is performed by <c>McpServerHub</c> in
    /// <c>oauth</c> mode (it has the validated token + hub-connection metadata) via
    /// <see cref="RegisterInstance"/>; the agent-facing selection tools + error TEXT arrive in b4.
    /// </summary>
    public sealed class AccountMcpStrategy : IMcpConnectionStrategy
    {
        readonly AccountInstances _instances;

        public AccountMcpStrategy() : this(new AccountInstances()) { }

        /// <summary>Testable ctor — inject a registry (e.g. with a deterministic clock).</summary>
        public AccountMcpStrategy(AccountInstances instances)
        {
            _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        }

        /// <summary>The account+instance registry backing this strategy.</summary>
        public AccountInstances Instances => _instances;

        public Consts.MCP.Server.AuthOption AuthOption => Consts.MCP.Server.AuthOption.oauth;

        public bool AllowMultipleConnections => true;

        public void Validate(DataArguments dataArguments)
        {
            if (string.IsNullOrWhiteSpace(dataArguments.AuthIssuer))
                throw new ArgumentException("auth=oauth mode requires --auth-issuer (the authorization server URL).");
            if (string.IsNullOrWhiteSpace(dataArguments.PublicUrl))
                throw new ArgumentException("auth=oauth mode requires --public-url (this server's canonical resource id).");
        }

        public void ConfigureAuthentication(TokenAuthenticationOptions options, DataArguments dataArguments)
        {
            // OAuth mode: the handler validates the presented token against the AS (JWKS +
            // introspection). No pre-shared ServerToken; RequireToken must be true so the handler
            // runs on the (RequireAuthorization-gated) MCP endpoint.
            options.OAuthMode = true;
            options.ServerToken = null;
            options.RequireToken = true;
        }

        // Plugin registration is driven by McpServerHub in oauth mode (RegisterInstance); the base
        // OnPluginConnected callback carries only the raw token, which is insufficient to establish
        // the validated account + instance metadata. Kept as a no-op so BaseHub's generic flow is
        // unchanged.
        public void OnPluginConnected(Type hubType, string connectionId, string? token, ILogger logger, Action<string, string?> disconnectClient)
        {
            logger.LogTrace("AccountMcpStrategy.OnPluginConnected: deferring account/instance registration to the hub. ConnectionId: {connectionId}.", connectionId);
        }

        public void OnPluginDisconnected(Type hubType, string connectionId, ILogger logger)
        {
            var removed = _instances.RemoveByConnection(connectionId);
            if (removed != null)
                logger.LogDebug("AccountMcpStrategy: instance {instanceId} (account {account}) removed on disconnect of {connectionId}.",
                    removed.Value.InstanceId, removed.Value.AccountId, connectionId);
        }

        /// <summary>
        /// Registers (or reconnect-replaces) a validated engine-plugin connection into the account's
        /// bucket. Called by <c>McpServerHub</c> in <c>oauth</c> mode after it validates the plugin's
        /// token (→ <paramref name="pluginIdentity"/>) and reads its handshake metadata.
        /// </summary>
        public PluginInstance RegisterInstance(ConnectionIdentity pluginIdentity, PluginInstanceMetadata metadata, string connectionId, ILogger logger)
        {
            if (pluginIdentity == null) throw new ArgumentNullException(nameof(pluginIdentity));
            var instance = _instances.Register(pluginIdentity.AccountId, metadata, connectionId);
            logger.LogDebug("AccountMcpStrategy: registered instance {instanceId} ({engine}:{project}) for account {account} on {connectionId}.",
                instance.InstanceId, instance.Engine, instance.ProjectName, pluginIdentity.AccountId, connectionId);
            return instance;
        }

        /// <summary>
        /// Resolves the CURRENT agent session (from the ambient <see cref="McpSessionTokenContext"/>)
        /// to a plugin instance, returning the full <see cref="InstanceResolution"/> so callers can
        /// surface the design-04 step-5 agent-actionable error variants (pinned-no-match vs
        /// account-empty). Fail-closed: no identity ⇒ <see cref="InstanceResolution.AccountEmpty"/>.
        /// </summary>
        public InstanceResolution ResolveCurrentSession()
        {
            var identity = McpSessionTokenContext.CurrentIdentity;
            if (identity == null)
                return InstanceResolution.AccountEmpty();

            return _instances.Resolve(
                identity.AccountId,
                McpSessionTokenContext.CurrentProjectPin,
                McpSessionTokenContext.CurrentSelectedInstanceId);
        }

        public string? ResolveConnectionId(string? token, int retryOffset)
        {
            // Account routing reads the resolved session context (identity + pin + sticky), NOT the
            // raw token. Fail closed when there is no identity (e.g. stdio without a JWT): never fall
            // back to another account's instance.
            var resolution = ResolveCurrentSession();

            if (resolution.Kind != InstanceResolutionKind.Resolved || resolution.Instance == null)
                return null;

            var connectionId = resolution.Instance.ConnectionId;
            _instances.TouchByConnection(connectionId); // bump MRU on every routed request
            return connectionId;
        }

        public bool ShouldNotifySession(string pluginConnectionId, string sessionId)
        {
            // Account isolation: a plugin's list-changed notification reaches a session only when the
            // session's account (sessionId == accountId in oauth) owns that plugin connection.
            if (string.IsNullOrEmpty(sessionId))
                return false;
            var pluginAccount = _instances.GetAccountByConnection(pluginConnectionId);
            return pluginAccount != null && string.Equals(pluginAccount, sessionId, StringComparison.Ordinal);
        }

        public NotificationTarget ResolveNotificationTarget(string? routingToken)
        {
            // routingToken == accountId in oauth. An agent-session lifecycle event reaches only the
            // account's OWN live instances — never another tenant's plugin (issue-#102 guarantee,
            // re-keyed by account). No account ⇒ drop (never broadcast).
            if (string.IsNullOrEmpty(routingToken))
                return NotificationTarget.Drop();

            var connectionIds = _instances.GetInstances(routingToken).Select(i => i.ConnectionId);
            return NotificationTarget.SpecificMany(connectionIds);
        }

        public McpClientData GetClientData(string? connectionId, IMcpSessionTracker sessionTracker)
        {
            var account = _instances.GetAccountByConnection(connectionId);
            if (account != null)
                return sessionTracker.GetClientDataByToken(account);
            // Deny unscoped access — an unregistered connection has no account.
            return new McpClientData();
        }

        public McpClientData[] GetAllClientData(string? connectionId, IMcpSessionTracker sessionTracker)
        {
            var account = _instances.GetAccountByConnection(connectionId);
            if (account != null)
                return sessionTracker.GetAllClientData(account).ToArray();
            return Array.Empty<McpClientData>();
        }

        public McpServerData GetServerData(string? connectionId, IMcpSessionTracker sessionTracker)
        {
            var account = _instances.GetAccountByConnection(connectionId);
            if (account != null)
                return sessionTracker.GetServerDataByToken(account);
            return new McpServerData();
        }

        /// <summary>
        /// Builds <see cref="PluginInstanceMetadata"/> from the plugin's hub-connection query values
        /// (design 04 / b7 wire format). When <paramref name="instanceId"/> is absent (pre-b7 plugin
        /// with no handshake payload) the connection id is used as a synthetic instance id so a
        /// single connection still auto-pairs; the empty project-path hash never matches a pin.
        /// </summary>
        public static PluginInstanceMetadata BuildInstanceMetadata(
            string connectionId,
            string? instanceId,
            string? engine,
            string? projectName,
            string? projectPathHash,
            string? machineName)
        {
            return new PluginInstanceMetadata(
                InstanceId: string.IsNullOrEmpty(instanceId) ? connectionId : instanceId!,
                Engine: engine ?? string.Empty,
                ProjectName: projectName ?? string.Empty,
                ProjectPathHash: projectPathHash ?? string.Empty,
                MachineName: machineName ?? string.Empty);
        }
    }
}
