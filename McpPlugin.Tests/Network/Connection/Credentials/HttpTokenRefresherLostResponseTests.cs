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
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection.Credentials
{
    /// <summary>
    /// A refresh whose response never reaches the client (timeout, dropped connection, backend
    /// restart) has usually already ROTATED the token on the server. The server absorbs a retry of
    /// that same refresh token only inside its rotation grace window (AI-Game-Dev-Server
    /// <c>OAUTH_ROTATION_GRACE_WINDOW_SECONDS</c> = 30 s); a later retry is reuse, which revokes the
    /// whole family. These tests run the refresher against a server emulator with exactly that rule.
    /// </summary>
    public sealed class HttpTokenRefresherLostResponseTests
    {
        const string ServerTarget = "https://ai-game.dev";
        const string ComponentClientId = "unity-mcp-plugin";
        static readonly TimeSpan ServerGrace = TimeSpan.FromSeconds(30);

        sealed class FakeClock
        {
            public DateTimeOffset Now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
            public void Advance(TimeSpan by) => Now += by;
        }

        /// <summary>Rotating refresh-token server with a reuse grace window and reuse ⇒ family revoke.</summary>
        sealed class RotatingServer : HttpMessageHandler
        {
            readonly FakeClock _clock;
            readonly Dictionary<string, (DateTimeOffset RotatedAt, string Successor)> _rotated = new Dictionary<string, (DateTimeOffset, string)>();
            int _generation;

            public RotatingServer(FakeClock clock) => _clock = clock;

            public string Head { get; private set; } = "RT0";
            public bool FamilyRevoked { get; private set; }
            public int Requests { get; private set; }
            public int DropResponses { get; set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests++;
                var body = await request.Content!.ReadAsStringAsync().ConfigureAwait(false);
                var presented = Uri.UnescapeDataString(ParseForm(body)["refresh_token"]);

                if (FamilyRevoked)
                    return Error("refresh token reuse detected; token family revoked");

                if (presented == Head)
                {
                    var successor = "RT" + (++_generation);
                    _rotated[presented] = (_clock.Now, successor);
                    Head = successor;
                    if (DropResponses > 0)
                    {
                        DropResponses--;
                        // The rotation is committed, but the client never sees the answer.
                        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                    }
                    return Success(successor);
                }

                if (_rotated.TryGetValue(presented, out var rotation)
                    && rotation.Successor == Head
                    && _clock.Now - rotation.RotatedAt <= ServerGrace)
                    return Success(rotation.Successor); // grace: same successor pair, idempotently

                FamilyRevoked = true;
                return Error("refresh token reuse detected; token family revoked");
            }

            static Dictionary<string, string> ParseForm(string body)
            {
                var form = new Dictionary<string, string>();
                foreach (var pair in body.Split('&'))
                {
                    var kv = pair.Split('=');
                    form[kv[0]] = kv.Length > 1 ? kv[1] : string.Empty;
                }
                return form;
            }

            static HttpResponseMessage Success(string refreshToken) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"access_token\":\"AT-" + refreshToken + "\",\"refresh_token\":\"" + refreshToken + "\",\"expires_in\":3600}", Encoding.UTF8, "application/json"),
            };

            static HttpResponseMessage Error(string description) => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"invalid_grant\",\"error_description\":\"" + description + "\"}", Encoding.UTF8, "application/json"),
            };
        }

        static HttpTokenRefresher NewRefresher(RotatingServer server, FakeClock clock)
            => new HttpTokenRefresher(ServerTarget, ComponentClientId, new HttpClient(server), () => clock.Now, attemptWindow: null, timeoutMs: 200);

        [Fact]
        public async Task LostResponse_RetryInsideServerGrace_RecoversTheSuccessor_AndKeepsTheFamily()
        {
            var clock = new FakeClock();
            var server = new RotatingServer(clock) { DropResponses = 1 };
            var refresher = NewRefresher(server, clock);

            var lost = await refresher.RefreshAsync(new TokenRefreshRequest("RT0", ServerTarget, ComponentClientId));
            lost.Succeeded.ShouldBeFalse();
            lost.FailureKind.ShouldBe(TokenRefreshFailureKind.Transient);

            // The connect loop retries 5 s later, well inside the server's grace window.
            clock.Advance(TimeSpan.FromSeconds(5));
            var retry = await refresher.RefreshAsync(new TokenRefreshRequest("RT0", ServerTarget, ComponentClientId));

            retry.Succeeded.ShouldBeTrue();
            retry.RefreshToken.ShouldBe("RT1");
            server.FamilyRevoked.ShouldBeFalse();
        }

        [Fact]
        public async Task LostResponse_NeverPresentsTheRotatedTokenAfterTheServerGrace()
        {
            var clock = new FakeClock();
            var server = new RotatingServer(clock) { DropResponses = 1 };
            var refresher = NewRefresher(server, clock);

            await refresher.RefreshAsync(new TokenRefreshRequest("RT0", ServerTarget, ComponentClientId));

            // Callers keep asking every 5 s (connect loop / recovery coordinator) for over a minute.
            TokenRefreshResult? last = null;
            for (var elapsed = 5; elapsed <= 75; elapsed += 5)
            {
                clock.Advance(TimeSpan.FromSeconds(5));
                last = await refresher.RefreshAsync(new TokenRefreshRequest("RT0", ServerTarget, ComponentClientId));
                if (last.Succeeded)
                    break;
            }

            server.FamilyRevoked.ShouldBeFalse();
            last.ShouldNotBeNull();
            last!.Succeeded.ShouldBeTrue();
        }
    }
}
