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
        public async Task OAuth_PluginExpires_ItsAccountsSessionsAreNotified_NoOtherAccountIs_AndItsReturnNotifiesAgain()
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
            await a1.WaitForCountAsync(3, Arrival);
            await a2.WaitForCountAsync(3, Arrival);
            await Task.Delay(Settle);
            a1.Methods.ShouldBe(new[] { ToolsChanged, PromptsChanged, ResourcesChanged }, ignoreOrder: true);
            a2.Methods.ShouldBe(new[] { ToolsChanged, PromptsChanged, ResourcesChanged }, ignoreOrder: true);
            b.Methods.ShouldBeEmpty("another account's plugin expired");

            // B's stream is live, and A's sessions do not hear B's plugin.
            PluginReconnect.Register(strategy, catalog, AccountB, "instance-b", "conn-b1");
            catalog.Record("conn-b1", PluginCatalogKind.Tools, PluginReconnect.Tools("engine_tool"));
            PluginReconnect.Unregister(strategy, catalog, "conn-b1");
            await b.WaitForCountAsync(1, Arrival);
            await Task.Delay(Settle);
            b.Methods.ShouldBe(new[] { ToolsChanged });
            a1.Methods.Count.ShouldBe(3);

            // A's plugin returns and sends its usual tool update: A's sessions hear it, B's do not.
            PluginReconnect.Register(strategy, catalog, AccountA, "instance-a", "conn-a2");
            var events = host.Services.GetRequiredService<HubEventToolsChange>();
            var update = new HubEventToolsChange.EventData { ConnectionId = "conn-a2", Request = new RequestToolsUpdated() };
            catalog.TryNotifyIfToolsChanged("conn-a2", () => events.OnNext(update)).ShouldBeFalse();
            events.OnNext(update);
            await a1.WaitForCountAsync(4, Arrival);
            await a2.WaitForCountAsync(4, Arrival);
            await Task.Delay(Settle);
            b.Methods.Count.ShouldBe(1);
        }

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

            await first.WaitForCountAsync(1, Arrival);
            await second.WaitForCountAsync(1, Arrival);
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

            public IReadOnlyList<string> Methods { get { lock (_methods) return _methods.ToArray(); } }

            public static async Task<NotificationStream> OpenAsync(HttpClient http, string baseUrl, string sessionId, string? bearerToken)
            {
                var stream = new NotificationStream();
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

            public async Task WaitForCountAsync(int count, TimeSpan timeout)
            {
                var deadline = DateTime.UtcNow + timeout;
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
