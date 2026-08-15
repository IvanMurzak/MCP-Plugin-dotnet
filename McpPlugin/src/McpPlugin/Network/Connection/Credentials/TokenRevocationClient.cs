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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.AgentConfig;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// The RFC 7009 revocation seam (unified-machine-auth F6): revokes one credential family's
    /// refresh token at <c>{serverTarget}/oauth/revoke</c>. Injected so the machine-wide sign-out
    /// helper's tests substitute a fake; <see cref="TokenRevocationClient"/> is the production
    /// implementation.
    /// </summary>
    public interface ITokenRevocationClient
    {
        /// <summary>
        /// Revoke <paramref name="token"/> (a refresh token), presenting <paramref name="clientId"/>
        /// when known (a legacy family of unknown id presents the component default — F6.2). Returns
        /// true when the server acknowledged (RFC 7009 answers 200 even for an unknown token, so
        /// true means "the server heard us", not "the token existed"); false on any transport or
        /// server failure — the caller decides how often to retry.
        /// </summary>
        Task<bool> RevokeAsync(string token, string? clientId, string? serverTarget, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Production RFC 7009 client: POSTs <c>token</c> + <c>token_type_hint=refresh_token</c>
    /// (+ <c>client_id</c> when known) to <c>{serverTarget}/oauth/revoke</c> with the shared 15 s
    /// HTTP timeout. Fails soft (false) — revocation is best-effort by design (F6.4: offline
    /// sign-out still deletes the local store; families die naturally at ≤30 d).
    /// </summary>
    public sealed class TokenRevocationClient : ITokenRevocationClient
    {
        /// <summary>Per-call HTTP timeout in ms — the shared contract value (04 §2).</summary>
        public const int HttpTimeoutMs = MachineCredentialLock.REFRESH_HTTP_TIMEOUT;

        static readonly HttpClient _sharedHttpClient = new HttpClient();

        readonly string _defaultServerTarget;
        readonly HttpClient _httpClient;
        readonly int _timeoutMs;

        /// <summary><paramref name="defaultServerTarget"/> — the AS root used when a call passes no target.</summary>
        public TokenRevocationClient(string defaultServerTarget, HttpClient? httpClient = null)
            : this(defaultServerTarget, httpClient, timeoutMs: null)
        {
        }

        /// <summary>Test seam (InternalsVisibleTo: McpPlugin.Tests): override the timeout.</summary>
        internal TokenRevocationClient(string defaultServerTarget, HttpClient? httpClient, int? timeoutMs)
        {
            _defaultServerTarget = HttpTokenRefresher.NormalizeServerTarget(defaultServerTarget) ?? string.Empty;
            _httpClient = httpClient ?? _sharedHttpClient;
            _timeoutMs = timeoutMs ?? HttpTimeoutMs;
        }

        /// <inheritdoc/>
        public async Task<bool> RevokeAsync(string token, string? clientId, string? serverTarget, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(token))
                return true; // nothing to revoke — vacuously acknowledged

            var baseUrl = HttpTokenRefresher.NormalizeServerTarget(serverTarget) ?? _defaultServerTarget;
            if (string.IsNullOrEmpty(baseUrl))
                return false;

            cancellationToken.ThrowIfCancellationRequested();

            var form = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("token", token),
                new KeyValuePair<string, string>("token_type_hint", "refresh_token"),
            };
            if (!string.IsNullOrEmpty(clientId))
                form.Add(new KeyValuePair<string, string>("client_id", clientId!));

            using var timeout = new CancellationTokenSource(_timeoutMs);
            try
            {
                using var content = new FormUrlEncodedContent(form);
                using var response = await _httpClient.PostAsync(baseUrl + "/oauth/revoke", content, timeout.Token).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception)
            {
                return false; // best-effort by design (F6.4); never token material in any log
            }
        }
    }
}
