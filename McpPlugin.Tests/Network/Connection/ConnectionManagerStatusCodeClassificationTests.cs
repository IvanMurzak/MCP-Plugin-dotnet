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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Moq;
using R3;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection
{
    /// <summary>
    /// oauth-client-error-hygiene 02 §C3.4 — the REAL <c>AttemptConnection</c> path (no fake
    /// override) against a live loopback socket, so the StatusCode classification itself is under
    /// test, not a test double's word for it:
    /// <list type="bullet">
    ///   <item>a server answering the SignalR negotiate with HTTP 401 must produce the
    ///   AuthRejected outcome, trip the 3-strike rejection cap, and fire
    ///   <c>OnAuthorizationRejected</c> (this test project runs net8.0/net9.0 — TFMs where
    ///   <c>HttpRequestException.StatusCode</c> is observable);</item>
    ///   <item>a connection-refused failure (no listener; the classifier sees an
    ///   HttpRequestException with a NULL StatusCode) must take the ordinary failure branch —
    ///   never the rejection path — which is also the behavioural shape the netstandard2.1
    ///   (Unity) TFM degrades to for EVERY failure, 401 included (its HttpRequestException has
    ///   no StatusCode property; the fallback there is proven by the netstandard2.1 build).</item>
    /// </list>
    /// </summary>
    public class ConnectionManagerStatusCodeClassificationTests
    {
        private readonly ILogger _logger;
        private readonly Common.Version _testVersion;

        public ConnectionManagerStatusCodeClassificationTests(ITestOutputHelper output)
        {
            var loggerFactory = TestLoggerFactory.Create(output, LogLevel.Trace);
            _logger = loggerFactory.CreateLogger<ConnectionManagerStatusCodeClassificationTests>();
            _testVersion = new Common.Version { Api = "1.0.0", Plugin = "1.0.0", Environment = "test" };
        }

        [Fact]
        public async Task Connect_401AtNegotiate_RealTransport_TripsRejectionCap_FiresAuthorizationRejected()
        {
            // Arrange: a real loopback HTTP server that answers EVERY request (the SignalR
            // negotiate POST) with 401 Unauthorized — the GlitchTip-#17 shape: a dead token
            // presented at negotiate.
            using var server = new LoopbackHttpStatusServer(HttpStatusCode.Unauthorized);
            var provider = new Mock<IHubConnectionProvider>();
            provider
                .Setup(x => x.CreateConnectionAsync(It.IsAny<string>()))
                .ReturnsAsync(() => new HubConnectionBuilder().WithUrl(server.Url).Build());

            await using var cm = new FastRetryConnectionManager(_logger, _testVersion, server.Url, provider.Object);

            var rejectionFired = 0;
            cm.OnAuthorizationRejected.Subscribe(_ => Interlocked.Increment(ref rejectionFired));

            // Act: the loop must terminate ON ITS OWN via the rejection cap; the CTS is a pure
            // hang guard that must NEVER fire (generous for loaded CI runners).
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await cm.Connect(cts.Token);

            // Assert
            cts.Token.IsCancellationRequested.ShouldBeFalse(
                "The loop must stop via the rejection cap, not the hang-guard CTS");
            result.ShouldBeFalse("Connection should fail after repeated 401 negotiates");
            rejectionFired.ShouldBe(1, "the observable 401s must aggregate into ONE 3-strike rejection signal");
            cm.KeepConnected.CurrentValue.ShouldBeFalse("KeepConnected must be disabled after the rejection cap");
            server.ConnectionCount.ShouldBe(3,
                "exactly MaxConsecutiveRejections (3) negotiate attempts must reach the server — " +
                "the observable-401 classification counts each toward the rejection cap");
        }

        [Fact]
        public async Task Connect_ConnectionRefused_RealTransport_TakesFailureBranch_NeverRejection()
        {
            // Arrange: a genuinely closed loopback port (bind, note the port, close). The real
            // AttemptConnection sees an HttpRequestException WITHOUT a StatusCode (socket-level
            // refusal) — the classifier must return Failed, so with the opt-in failure cap the
            // loop stops through the FAILURE branch and the rejection signal stays silent.
            // Behaviourally this is the same degradation the Unity TFM applies to every failure.
            var closedPort = LoopbackHttpStatusServer.ReserveClosedPort();
            var url = $"http://127.0.0.1:{closedPort}";
            var provider = new Mock<IHubConnectionProvider>();
            provider
                .Setup(x => x.CreateConnectionAsync(It.IsAny<string>()))
                .ReturnsAsync(() => new HubConnectionBuilder().WithUrl(url).Build());

            await using var cm = new FastRetryConnectionManager(_logger, _testVersion, url, provider.Object,
                maxConsecutiveConnectionFailures: 2);

            var rejectionFired = false;
            cm.OnAuthorizationRejected.Subscribe(_ => rejectionFired = true);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await cm.Connect(cts.Token);

            // Assert: stopped by the failure cap on its own (were these classified AuthRejected,
            // consecutiveFailures would stay 0, the failure cap could never fire, and the loop
            // would instead stop via the rejection cap — with the signal fired).
            cts.Token.IsCancellationRequested.ShouldBeFalse(
                "The loop must stop via the opt-in failure cap, not the hang-guard CTS");
            result.ShouldBeFalse("Connection should fail (nothing is listening)");
            rejectionFired.ShouldBeFalse(
                "A connection-refused failure has no observable 401/403 — it must never count as an auth rejection");
            cm.KeepConnected.CurrentValue.ShouldBeFalse("The failure cap disables KeepConnected");
        }

        [Fact]
        public async Task Connect_ConnectionRefused_RealTransport_DefaultConfig_RetriesUnbounded()
        {
            // Control (binding testing strategy, 02): with DEFAULT config (no failure cap) a
            // transient connection-refused failure must keep retrying unbounded — only the
            // external CTS stops the loop, and reconnection intent (KeepConnected) survives.
            var closedPort = LoopbackHttpStatusServer.ReserveClosedPort();
            var url = $"http://127.0.0.1:{closedPort}";
            var provider = new Mock<IHubConnectionProvider>();
            provider
                .Setup(x => x.CreateConnectionAsync(It.IsAny<string>()))
                .ReturnsAsync(() => new HubConnectionBuilder().WithUrl(url).Build());

            await using var cm = new FastRetryConnectionManager(_logger, _testVersion, url, provider.Object);

            var rejectionFired = false;
            cm.OnAuthorizationRejected.Subscribe(_ => rejectionFired = true);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var result = await cm.Connect(cts.Token);

            result.ShouldBeFalse("Connection should fail (nothing is listening)");
            cts.Token.IsCancellationRequested.ShouldBeTrue(
                "With default (unlimited) config only the external CTS may stop the loop");
            rejectionFired.ShouldBeFalse("Refused connections must never fire the rejection signal");
            cm.KeepConnected.CurrentValue.ShouldBeTrue(
                "Default (unlimited) retry must NOT disable KeepConnected on its own");
        }

        /// <summary>
        /// A ConnectionManager with REAL AttemptConnection (the code under test) and only the
        /// retry pacing accelerated so the tests do not spend 5 s between attempts.
        /// </summary>
        private class FastRetryConnectionManager : ConnectionManager
        {
            protected override TimeSpan RejectionThreshold { get; } = TimeSpan.FromMilliseconds(50);

            public FastRetryConnectionManager(
                ILogger logger, Common.Version version, string endpoint, IHubConnectionProvider provider,
                int maxConsecutiveConnectionFailures = 0)
                : base(logger, version, endpoint, provider, maxConsecutiveConnectionFailures)
            {
            }

            protected override Task WaitBeforeRetry(CancellationToken cancellationToken)
                => Task.CompletedTask;
        }

        /// <summary>
        /// Minimal loopback HTTP server (raw TcpListener — no URL ACL, no ASP.NET dependency):
        /// reads one request's headers and answers with a fixed status code, then closes the
        /// connection. Enough for the SignalR client's negotiate POST.
        /// </summary>
        private sealed class LoopbackHttpStatusServer : IDisposable
        {
            readonly TcpListener _listener;
            readonly HttpStatusCode _status;
            int _connectionCount;
            volatile bool _disposed;

            public LoopbackHttpStatusServer(HttpStatusCode status)
            {
                _status = status;
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                _ = AcceptLoopAsync();
            }

            public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

            /// <summary>How many TCP connections (≈ HTTP requests, Connection: close) were served.</summary>
            public int ConnectionCount => _connectionCount;

            /// <summary>Bind an ephemeral loopback port, close it, and return it — a port that refuses connections.</summary>
            public static int ReserveClosedPort()
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                var port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                return port;
            }

            async Task AcceptLoopAsync()
            {
                try
                {
                    while (!_disposed)
                    {
                        var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                        Interlocked.Increment(ref _connectionCount);
                        _ = ServeAsync(client);
                    }
                }
                catch (ObjectDisposedException) { }
                catch (SocketException) { } // listener stopped
            }

            async Task ServeAsync(TcpClient client)
            {
                try
                {
                    using (client)
                    using (var stream = client.GetStream())
                    {
                        // Read until the end of the request headers (the negotiate POST has no body).
                        var buffer = new byte[4096];
                        var received = new StringBuilder();
                        while (!received.ToString().Contains("\r\n\r\n"))
                        {
                            var read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                            if (read <= 0)
                                break;
                            received.Append(Encoding.ASCII.GetString(buffer, 0, read));
                            if (received.Length > 64 * 1024)
                                break; // defensive bound
                        }

                        var response = $"HTTP/1.1 {(int)_status} {_status}\r\n" +
                                       "Content-Length: 0\r\n" +
                                       "Connection: close\r\n" +
                                       "\r\n";
                        var bytes = Encoding.ASCII.GetBytes(response);
                        await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                        await stream.FlushAsync().ConfigureAwait(false);
                    }
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                catch (SocketException) { }
            }

            public void Dispose()
            {
                _disposed = true;
                try { _listener.Stop(); } catch { }
            }
        }
    }
}
