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
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// A plugin that stays gone past the grace, seen by real MCP sessions: their open notification
    /// streams get the list-changed notifications with no client traffic, in the right account only.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class PluginExpiryNotificationOverHttpTests
    {
        const string AccountA = "acct-expiry-a";
        const string AccountB = "acct-expiry-b";
        const string ToolsChanged = "notifications/tools/list_changed";
        const string PromptsChanged = "notifications/prompts/list_changed";
        const string ResourcesChanged = "notifications/resources/list_changed";
        static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(700);
        static readonly TimeSpan Arrival = TimeSpan.FromSeconds(15);

        [Fact]
        public async Task OAuth_PluginExpires_ItsAccountsSessionsAreNotified_NoOtherAccountIs()
        {
            await using var host = await OAuthMcpHost.StartAsync(null, AccountA, "plugin-reconnect-grace-seconds=1");
            var strategy = (AccountMcpStrategy)host.Services.GetRequiredService<IMcpConnectionStrategy>();
            var catalog = host.Services.GetRequiredService<PluginCatalogCache>();
            using var http = new HttpClient();
            var tokenA = host.TokenForAccount(AccountA);
            var tokenB = host.TokenForAccount(AccountB);

            await using var a1 = await NotificationStream.OpenAsync(http, host.BaseUrl, await host.HandshakeAsync(http, bearerToken: tokenA), tokenA);
            await using var a2 = await NotificationStream.OpenAsync(http, host.BaseUrl, await host.HandshakeAsync(http, bearerToken: tokenA), tokenA);
            await using var b = await NotificationStream.OpenAsync(http, host.BaseUrl, await host.HandshakeAsync(http, bearerToken: tokenB), tokenB);

            PluginReconnect.Register(strategy, catalog, AccountA, "instance-a", "conn-a1");
            catalog.Record("conn-a1", PluginCatalogKind.Tools, PluginReconnect.Tools("engine_tool"));
            catalog.Record("conn-a1", PluginCatalogKind.Prompts, new ResponseListPrompts());
            catalog.Record("conn-a1", PluginCatalogKind.Resources, Array.Empty<ResponseListResource>());
            catalog.Record("conn-a1", PluginCatalogKind.ResourceTemplates, Array.Empty<ResponseResourceTemplate>());
            PluginReconnect.Unregister(strategy, catalog, "conn-a1");

            // No request is sent from here on: only the timer can cause these.
            await a1.WaitForCountAsync(3);
            await a2.WaitForCountAsync(3);
            await Task.Delay(Settle);
            a1.Methods.ShouldBe(new[] { ToolsChanged, PromptsChanged, ResourcesChanged }, ignoreOrder: true);
            a2.Methods.ShouldBe(new[] { ToolsChanged, PromptsChanged, ResourcesChanged }, ignoreOrder: true);
            b.Methods.ShouldBeEmpty("another account's plugin expired");

            // B's stream is live, and A's sessions do not hear B's plugin.
            PluginReconnect.Register(strategy, catalog, AccountB, "instance-b", "conn-b1");
            catalog.Record("conn-b1", PluginCatalogKind.Tools, PluginReconnect.Tools("engine_tool"));
            PluginReconnect.Unregister(strategy, catalog, "conn-b1");
            await b.WaitForCountAsync(1);
            await Task.Delay(Settle);
            b.Methods.ShouldBe(new[] { ToolsChanged });
            a1.Methods.Count.ShouldBe(3);

            // These streams now carry notifications, so another account's token must not open one.
            using var foreign = new HttpRequestMessage(HttpMethod.Get, host.BaseUrl + "/mcp");
            foreign.Headers.Accept.ParseAdd("text/event-stream");
            foreign.Headers.TryAddWithoutValidation("Mcp-Session-Id", a1.SessionId);
            foreign.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
            using var refused = await http.SendAsync(foreign, HttpCompletionOption.ResponseHeadersRead);
            refused.IsSuccessStatusCode.ShouldBeFalse();
        }

        /// <summary>
        /// Through the real hub, over a real plugin connection: a returning plugin announces only its
        /// tools, yet agents were told its prompts and resources left too.
        /// </summary>
        [Fact]
        public async Task NoneAuth_PluginReturnsAfterTheExpiry_EveryListGoneIsAnnouncedAgain_Once()
        {
            await using var host = await NoneAuthMcpHost.StartAsync(null, "plugin-reconnect-grace-seconds=1");
            var catalog = host.Services.GetRequiredService<PluginCatalogCache>();
            using var http = new HttpClient();
            await using var session = await NotificationStream.OpenAsync(http, host.BaseUrl, await host.HandshakeAsync(http), bearerToken: null);

            await using var plugin = new FakeEnginePlugin(host.BaseUrl, "editor", Environment);
            await plugin.StartAsync();
            var connection = plugin.ConnectionId!;
            catalog.Record(connection, PluginCatalogKind.Tools, PluginReconnect.Tools("engine_tool"));
            catalog.Record(connection, PluginCatalogKind.Prompts, new ResponseListPrompts());
            catalog.Record(connection, PluginCatalogKind.Resources, Array.Empty<ResponseListResource>());
            await plugin.StopAsync();
            await session.WaitForCountAsync(3);
            await Task.Delay(Settle);
            session.Methods.ShouldBe(new[] { ToolsChanged, PromptsChanged, ResourcesChanged }, ignoreOrder: true);

            await using var back = new FakeEnginePlugin(host.BaseUrl, "editor", Environment);
            await back.StartAsync();
            await back.NotifyToolsUpdatedAsync();
            await session.WaitForCountAsync(6);
            await Task.Delay(Settle);
            session.Methods.Skip(3).ShouldBe(new[] { ToolsChanged, PromptsChanged, ResourcesChanged }, ignoreOrder: true);

            await back.NotifyToolsUpdatedAsync();
            await session.WaitForCountAsync(7);
            await Task.Delay(Settle);
            session.Methods.Count.ShouldBe(7, "the other lists are announced again once, not on every tool update");
        }

        const string Environment = "Godot 4.5.1.stable";

        [Fact]
        public async Task OAuth_PluginReturnsWithinTheGrace_NoSessionIsNotified()
        {
            await using var host = await OAuthMcpHost.StartAsync(null, AccountA, "plugin-reconnect-grace-seconds=1");
            var strategy = (AccountMcpStrategy)host.Services.GetRequiredService<IMcpConnectionStrategy>();
            var catalog = host.Services.GetRequiredService<PluginCatalogCache>();
            using var http = new HttpClient();
            var token = host.TokenForAccount(AccountA);
            await using var a = await NotificationStream.OpenAsync(http, host.BaseUrl, await host.HandshakeAsync(http, bearerToken: token), token);

            PluginReconnect.Register(strategy, catalog, AccountA, "instance-a", "conn-a1");
            catalog.Record("conn-a1", PluginCatalogKind.Tools, PluginReconnect.Tools("engine_tool"));
            PluginReconnect.Unregister(strategy, catalog, "conn-a1");
            PluginReconnect.Register(strategy, catalog, AccountA, "instance-a", "conn-a2");

            await Task.Delay(TimeSpan.FromSeconds(2));

            a.Methods.ShouldBeEmpty();

            // Control: this stream does deliver an expiry.
            PluginReconnect.Unregister(strategy, catalog, "conn-a2");
            await a.WaitForCountAsync(1);
        }

        [Fact]
        public async Task NoneAuth_PluginExpires_EverySessionIsNotified()
        {
            await using var host = await NoneAuthMcpHost.StartAsync(null, "plugin-reconnect-grace-seconds=1");
            var catalog = host.Services.GetRequiredService<PluginCatalogCache>();
            using var http = new HttpClient();
            await using var first = await NotificationStream.OpenAsync(http, host.BaseUrl, await host.HandshakeAsync(http), bearerToken: null);
            await using var second = await NotificationStream.OpenAsync(http, host.BaseUrl, await host.HandshakeAsync(http), bearerToken: null);

            var connection = "expiry-none-" + Guid.NewGuid().ToString("N");
            ClientUtils.AddClient(typeof(McpServerHub), connection, NullLogger.Instance);
            try
            {
                catalog.OnPluginConnected(connection);
                catalog.Record(connection, PluginCatalogKind.Tools, PluginReconnect.Tools("engine_tool"));
                catalog.OnPluginDisconnected(connection);
            }
            finally
            {
                ClientUtils.RemoveClient(typeof(McpServerHub), connection, NullLogger.Instance);
            }

            await first.WaitForCountAsync(1);
            await second.WaitForCountAsync(1);
            await Task.Delay(Settle);
            first.Methods.ShouldBe(new[] { ToolsChanged });
            second.Methods.ShouldBe(new[] { ToolsChanged });
        }

        /// <summary>A session's open GET stream, collecting the method of every message it delivers.</summary>
        sealed class NotificationStream : IAsyncDisposable
        {
            readonly CancellationTokenSource _stop = new CancellationTokenSource();
            readonly List<string> _methods = new List<string>();
            HttpResponseMessage? _response;
            Task? _reader;

            public string SessionId { get; private set; } = string.Empty;

            public IReadOnlyList<string> Methods { get { lock (_methods) return _methods.ToArray(); } }

            public static async Task<NotificationStream> OpenAsync(HttpClient http, string baseUrl, string sessionId, string? bearerToken)
            {
                var stream = new NotificationStream { SessionId = sessionId };
                using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/mcp");
                request.Headers.Accept.ParseAdd("text/event-stream");
                request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
                if (bearerToken != null)
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                stream._response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                stream._response.IsSuccessStatusCode.ShouldBeTrue($"GET /mcp failed: {(int)stream._response.StatusCode}");
                stream._reader = stream.ReadAsync(await stream._response.Content.ReadAsStreamAsync());
                return stream;
            }

            async Task ReadAsync(Stream body)
            {
                try
                {
                    using var reader = new StreamReader(body);
                    while (await reader.ReadLineAsync(_stop.Token) is { } line)
                    {
                        if (!line.StartsWith("data:", StringComparison.Ordinal))
                            continue;
                        using var json = JsonDocument.Parse(line.Substring(5).Trim());
                        if (json.RootElement.TryGetProperty("method", out var method))
                            lock (_methods) _methods.Add(method.GetString()!);
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or HttpRequestException)
                {
                    // The stream closed: the test or the host ended.
                }
            }

            public async Task WaitForCountAsync(int count)
            {
                var deadline = DateTime.UtcNow + Arrival;
                while (Methods.Count < count && DateTime.UtcNow < deadline)
                    await Task.Delay(25);
                Methods.Count.ShouldBeGreaterThanOrEqualTo(count, "got: " + string.Join(", ", Methods));
            }

            public async ValueTask DisposeAsync()
            {
                _stop.Cancel();
                _response?.Dispose();
                if (_reader != null)
                    await _reader;
                _stop.Dispose();
            }
        }
    }
}
