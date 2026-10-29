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
using System.Linq;
using System.Threading;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server.Strategy
{
    /// <summary>
    /// The single-project strategies (<c>none</c>, <c>token</c>): one engine project, so one editor plugin
    /// connection, plus one runtime (game) connection beside it.
    /// <list type="bullet">
    ///   <item>A new connection replaces only an older connection of the SAME <see cref="ClientRole"/>: a game
    ///   that starts never disconnects the editor, and an editor that reconnects after a reload still
    ///   replaces its stale connection.</item>
    ///   <item>The role is the one the plugin declares at connect (hub query <c>client_role</c> or header
    ///   <c>X-Client-Role</c>), else the version handshake's <c>environment</c>
    ///   (<see cref="ClientRoles.FromEnvironment"/>, for plugins that predate <c>client_role</c>). A connection
    ///   whose role is not known yet replaces nothing and is never routed to.</item>
    ///   <item>Agents are routed to the editor. A runtime serves only when no editor is connected and none
    ///   disconnected within <see cref="EditorReconnectWindow"/> (a standalone game build), so calls made
    ///   during an editor reload wait for the editor instead of reaching the game. The exception is a call of
    ///   a tool only the runtime has (<see cref="PluginRoleRouting"/>): it goes to the runtime.</item>
    ///   <item>Only editors enter <see cref="ClientUtils"/>' plugin list, so the remembered lists of
    ///   <see cref="PluginCatalogCache"/> stay the editor's.</item>
    /// </list>
    /// </summary>
    public abstract class SingleProjectMcpStrategy : IMcpConnectionStrategy, IPluginRoleStrategy
    {
        static readonly Type RoutedHubType = typeof(McpServerHub);

        /// <summary>Keeps a role change and the matching ClientUtils change together (two editors may classify at once).</summary>
        readonly object _gate = new object();

        TimeSpan _editorReconnectWindow = TimeSpan.FromSeconds(Consts.MCP.Server.DefaultPluginReconnectWaitSeconds);
        int _runtimeServed;

        public abstract Consts.MCP.Server.AuthOption AuthOption { get; }

        // One routing slot from the library's point of view: only editors are in ClientUtils' list.
        public bool AllowMultipleConnections => false;

        public PluginConnectionRegistry Registry { get; } = new PluginConnectionRegistry();

        /// <summary>
        /// How long after an editor disconnects a runtime is still not used in its place. Set from the
        /// plugin reconnect wait in <see cref="Validate"/>.
        /// </summary>
        public TimeSpan EditorReconnectWindow
        {
            get => _editorReconnectWindow;
            set => _editorReconnectWindow = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        }

        public virtual void Validate(DataArguments dataArguments)
        {
            EditorReconnectWindow = dataArguments.GetPluginReconnectWait();
        }

        public abstract void ConfigureAuthentication(TokenAuthenticationOptions options, DataArguments dataArguments);

        public void OnPluginConnected(Type hubType, string connectionId, string? token, ILogger logger, Action<string, string?> disconnectClient)
            => OnPluginConnected(hubType, connectionId, token, declaredRole: null, logger, disconnectClient);

        public virtual void OnPluginConnected(Type hubType, string connectionId, string? token, ClientRole? declaredRole,
            ILogger logger, Action<string, string?> disconnectClient)
        {
            logger.LogInformation("auth={0}: plugin connection '{1}' connected (role: {2}).", AuthOption, connectionId, Describe(declaredRole));
            lock (_gate)
            {
                var replaced = Registry.Add(connectionId, declaredRole, token);
                if (declaredRole == ClientRole.Editor && !replaced.Contains(connectionId))
                    ClientUtils.AddClient(hubType, connectionId, logger, token);
                Disconnect(hubType, replaced, declaredRole, logger, disconnectClient);
            }
        }

        public bool OnPluginHandshake(Type hubType, string connectionId, string? environment, ILogger logger, Action<string, string?> disconnectClient)
        {
            var role = ClientRoles.FromEnvironment(environment);
            lock (_gate)
            {
                if (!Registry.TryClassify(connectionId, role, out var token, out var replaced))
                    return false;

                logger.LogInformation("auth={0}: plugin connection '{1}' is {2} (handshake environment '{3}').", AuthOption, connectionId, Describe(role), environment);
                var routedEditor = role == ClientRole.Editor && !replaced.Contains(connectionId);
                if (routedEditor)
                    ClientUtils.AddClient(hubType, connectionId, logger, token);
                Disconnect(hubType, replaced, role, logger, disconnectClient);
                return routedEditor;
            }
        }

        void Disconnect(Type hubType, IReadOnlyList<string> replaced, ClientRole? role, ILogger logger, Action<string, string?> disconnectClient)
        {
            foreach (var id in replaced)
            {
                logger.LogInformation("auth={0}: disconnecting {1} connection '{2}' (replaced by a newer one).", AuthOption, Describe(role), id);
                // Removed at once, so routing cannot pick it before OnPluginDisconnected fires.
                if (ClientUtils.IsConnected(hubType, id))
                    ClientUtils.RemoveClient(hubType, id, logger);
                disconnectClient(id, $"A newer {Describe(role)} connection has replaced this one (auth={AuthOption} keeps one editor and one runtime connection).");
            }
        }

        public virtual void OnPluginDisconnected(Type hubType, string connectionId, ILogger logger)
        {
            lock (_gate)
            {
                Registry.Remove(connectionId, out _);
                if (ClientUtils.IsConnected(hubType, connectionId))
                    ClientUtils.RemoveClient(hubType, connectionId, logger);
            }
        }

        /// <summary>
        /// The editor (by token, else the best one); else, unless an editor left within <see cref="EditorReconnectWindow"/>, a runtime.
        /// A request routed to the runtime (<see cref="PluginRoleRouting.Preferred"/>) gets the runtime while one is connected.
        /// </summary>
        public string? ResolveConnectionId(string? token, int retryOffset)
        {
            if (PluginRoleRouting.Preferred == ClientRole.Runtime)
            {
                var preferred = Registry.WithRole(ClientRole.Runtime);
                if (preferred.Count > 0)
                    return preferred[Math.Abs(retryOffset) % preferred.Count];
            }

            var editor = ClientUtils.GetConnectionIdByToken(token) ?? ClientUtils.GetBestConnectionId(RoutedHubType, retryOffset);
            if (editor != null || Registry.EditorLeftWithin(EditorReconnectWindow))
                return editor;

            var runtimes = Registry.WithRole(ClientRole.Runtime);
            if (runtimes.Count == 0)
                return null;
            Interlocked.Exchange(ref _runtimeServed, 1);
            return runtimes[Math.Abs(retryOffset) % runtimes.Count];
        }

        public bool TakeRuntimeServed() => Interlocked.Exchange(ref _runtimeServed, 0) == 1;

        /// <summary>
        /// Every connection's list changes reach agents: a runtime's tools that the editor does not have are part of
        /// <c>tools/list</c> while an editor is connected too (see <see cref="RemoteToolRunner"/>).
        /// </summary>
        public bool ShouldNotifySession(string pluginConnectionId, string sessionId) => true;

        // One project: a broadcast reaches at most the one editor the agents are routed to.
        public NotificationTarget ResolveNotificationTarget(string? routingToken) => NotificationTarget.Broadcast();

        public McpClientData GetClientData(string? connectionId, IMcpSessionTracker sessionTracker)
            => sessionTracker.GetClientData();

        public McpClientData[] GetAllClientData(string? connectionId, IMcpSessionTracker sessionTracker)
            => sessionTracker.GetAllClientData().ToArray();

        public McpServerData GetServerData(string? connectionId, IMcpSessionTracker sessionTracker)
            => sessionTracker.GetServerData();

        static string Describe(ClientRole? role) => role.HasValue ? ClientRoles.ToWire(role.Value) : "unknown-role";
    }
}
