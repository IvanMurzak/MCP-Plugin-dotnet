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
using System.Linq;
using System.Text.Json;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure
{
    /// <summary>
    /// A fake <see cref="IHubContext{McpServerHub}"/> whose plugin connections are added and removed by
    /// the test. An unknown connection id resolves to null, like a connection that is gone.
    /// </summary>
    public sealed class FakePluginHub
    {
        readonly ConcurrentDictionary<string, Mock<ISingleClientProxy>> _plugins = new ConcurrentDictionary<string, Mock<ISingleClientProxy>>();

        public FakePluginHub()
        {
            var clients = new Mock<IHubClients>();
            clients.Setup(c => c.Client(It.IsAny<string>()))
                .Returns((string id) => _plugins.TryGetValue(id, out var plugin) ? plugin.Object : null!);
            var hub = new Mock<IHubContext<McpServerHub>>();
            hub.Setup(h => h.Clients).Returns(clients.Object);
            Context = hub.Object;
        }

        public IHubContext<McpServerHub> Context { get; }

        public Mock<ISingleClientProxy> this[string connectionId] => _plugins[connectionId];

        public Mock<ISingleClientProxy> Add(string connectionId)
        {
            var plugin = new Mock<ISingleClientProxy>();
            _plugins[connectionId] = plugin;
            return plugin;
        }
    }

    /// <summary>Helpers for plugin reconnect tests: tool lists, agent sessions, instance (un)registration.</summary>
    public static class PluginReconnect
    {
        public const string HashA = "aabbccdd11223344556677889900aabbccddeeff00112233445566778899aabb";

        public static ResponseListTool[] Tools(params string[] names)
            => names.Select(n => new ResponseListTool
            {
                Name = n,
                Description = "tool " + n,
                InputSchema = JsonDocument.Parse("{\"type\":\"object\"}").RootElement.Clone(),
            }).ToArray();

        /// <summary>Sets the ambient agent session (identity, pin, selection) until disposed.</summary>
        public static IDisposable Session(string? account, string? pin = null, string? selected = null)
        {
            McpSessionTokenContext.CurrentIdentity = account == null ? null : new ConnectionIdentity(account, ConnectionIdentity.RoleAgent);
            McpSessionTokenContext.CurrentProjectPin = pin;
            McpSessionTokenContext.CurrentSelectedInstanceId = selected;
            return new ResetSession();
        }

        /// <summary>Registers a plugin instance with the strategy and tells the cache, as the hub does.</summary>
        public static void Register(AccountMcpStrategy strategy, PluginCatalogCache catalog, string account, string instanceId,
            string connectionId, string pathHash = HashA, string machine = "PC-1")
        {
            strategy.RegisterInstance(new ConnectionIdentity(account, ConnectionIdentity.RolePlugin),
                new PluginInstanceMetadata(instanceId, "unity", "MyGame", pathHash, machine), connectionId, NullLogger.Instance);
            catalog.OnPluginConnected(connectionId);
        }

        /// <summary>Drops a plugin connection, in the hub's order: cache first, then the strategy.</summary>
        public static void Unregister(AccountMcpStrategy strategy, PluginCatalogCache catalog, string connectionId)
        {
            catalog.OnPluginDisconnected(connectionId);
            strategy.OnPluginDisconnected(typeof(McpServerHub), connectionId, NullLogger.Instance);
        }

        sealed class ResetSession : IDisposable
        {
            public void Dispose()
            {
                McpSessionTokenContext.CurrentIdentity = null;
                McpSessionTokenContext.CurrentProjectPin = null;
                McpSessionTokenContext.CurrentSelectedInstanceId = null;
            }
        }
    }
}
