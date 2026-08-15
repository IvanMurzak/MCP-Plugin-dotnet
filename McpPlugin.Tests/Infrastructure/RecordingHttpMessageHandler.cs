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
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace com.IvanMurzak.McpPlugin.Tests.Infrastructure
{
    /// <summary>
    /// An <see cref="HttpMessageHandler"/> that records every request (URL + decoded form fields)
    /// and replays scripted responses — the wire-assertion seam for the unified-machine-auth b3
    /// HTTP clients (<c>HttpTokenRefresher</c>, <c>TokenExchangeClient</c>,
    /// <c>TokenRevocationClient</c>). No sockets, no ports (workspace rule: workers bind only
    /// assigned ports — this binds none).
    /// </summary>
    public sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        readonly Queue<Func<RecordedRequest, HttpResponseMessage>> _scripted = new Queue<Func<RecordedRequest, HttpResponseMessage>>();
        readonly List<RecordedRequest> _requests = new List<RecordedRequest>();
        Func<RecordedRequest, HttpResponseMessage>? _fallback;
        TimeSpan _delay = TimeSpan.Zero;
        Action<RecordedRequest>? _onRequest;

        /// <summary>One recorded request: absolute URL and the decoded form body.</summary>
        public sealed class RecordedRequest
        {
            public RecordedRequest(string url, IReadOnlyDictionary<string, string> form)
            {
                Url = url;
                Form = form;
            }

            public string Url { get; }
            public IReadOnlyDictionary<string, string> Form { get; }
        }

        /// <summary>Every request this handler served, in order.</summary>
        public IReadOnlyList<RecordedRequest> Requests => _requests;

        /// <summary>Enqueue one scripted response (consumed in FIFO order before <see cref="RespondWith"/>).</summary>
        public RecordingHttpMessageHandler Enqueue(HttpStatusCode status, string json)
        {
            _scripted.Enqueue(_ => JsonResponse(status, json));
            return this;
        }

        /// <summary>The response used when the scripted queue is empty.</summary>
        public RecordingHttpMessageHandler RespondWith(HttpStatusCode status, string json)
        {
            _fallback = _ => JsonResponse(status, json);
            return this;
        }

        /// <summary>Delay every response (for timeout tests).</summary>
        public RecordingHttpMessageHandler DelayEachResponse(TimeSpan delay)
        {
            _delay = delay;
            return this;
        }

        /// <summary>Invoked synchronously as each request is recorded (e.g. to displace a lock file mid-call).</summary>
        public RecordingHttpMessageHandler OnRequest(Action<RecordedRequest> callback)
        {
            _onRequest = callback;
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
            var recorded = new RecordedRequest(request.RequestUri?.ToString() ?? "", DecodeForm(body));
            lock (_requests)
                _requests.Add(recorded);
            _onRequest?.Invoke(recorded);

            if (_delay > TimeSpan.Zero)
                await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);

            Func<RecordedRequest, HttpResponseMessage>? producer;
            lock (_requests)
                producer = _scripted.Count > 0 ? _scripted.Dequeue() : _fallback;
            if (producer == null)
                throw new InvalidOperationException("RecordingHttpMessageHandler: no scripted response left and no fallback configured.");
            return producer(recorded);
        }

        static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
            => new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

        static IReadOnlyDictionary<string, string> DecodeForm(string body)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in body.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                var key = eq < 0 ? pair : pair.Substring(0, eq);
                var value = eq < 0 ? "" : pair.Substring(eq + 1);
                result[Uri.UnescapeDataString(key.Replace('+', ' '))] = Uri.UnescapeDataString(value.Replace('+', ' '));
            }
            return result;
        }
    }

    /// <summary>
    /// A deterministic scripted <see cref="ITokenRefresher"/> for provider tests. Hand-rolled (not
    /// Moq) on purpose: the interface now carries a default implementation for the request-based
    /// overload, and a hand-written fake pins EXACTLY which overload the provider invokes —
    /// proxy-generated mocks leave that dispatch ambiguous.
    /// </summary>
    public sealed class FakeTokenRefresher : ITokenRefresher
    {
        readonly Func<TokenRefreshRequest, TokenRefreshResult> _onRefresh;
        readonly List<TokenRefreshRequest> _requests = new List<TokenRefreshRequest>();

        public FakeTokenRefresher(Func<TokenRefreshRequest, TokenRefreshResult> onRefresh)
        {
            _onRefresh = onRefresh;
        }

        public FakeTokenRefresher(TokenRefreshResult fixedResult)
            : this(_ => fixedResult)
        {
        }

        /// <summary>Every request-based refresh call the provider made, in order.</summary>
        public IReadOnlyList<TokenRefreshRequest> Requests => _requests;

        /// <summary>How many times the LEGACY two-string API was called (must stay 0 — the provider uses the family-aware API).</summary>
        public int LegacyCalls { get; private set; }

        public Task<TokenRefreshResult> RefreshAsync(string refreshToken, string? serverTarget, CancellationToken cancellationToken = default)
        {
            LegacyCalls++;
            return Task.FromResult(_onRefresh(new TokenRefreshRequest(refreshToken, serverTarget, clientId: null)));
        }

        public Task<TokenRefreshResult> RefreshAsync(TokenRefreshRequest request, CancellationToken cancellationToken = default)
        {
            _requests.Add(request);
            return Task.FromResult(_onRefresh(request));
        }
    }
}
