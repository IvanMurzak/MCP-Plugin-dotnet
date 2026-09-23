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
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.AgentConfig
{
    /// <summary>
    /// Gets (or mints) the Cloud <b>project key</b> the agent-config writers put into
    /// <c>Authorization: Bearer agd_pk_…</c> (project-keys contract §2 / §6). Engine-agnostic: Unity uses it
    /// in-process, the Godot addon and the Unreal .NET bridge the same way.
    ///
    /// <para><b>Get-or-mint</b> (<see cref="GetOrMintAsync"/>): look up <c>&lt;issuerOrigin&gt;#&lt;pin&gt;</c> in
    /// <see cref="ProjectKeyStore"/>; reuse it when its <c>sub</c> is the signed-in account AND
    /// <c>GET /api/mcp/project-keys/current</c> accepts it (a transient, non-401 failure of that check also
    /// reuses it); otherwise mint a NEW key with <c>POST /api/mcp/project-keys</c> using the machine access
    /// token, cache it, and return it. No machine login ⇒ <c>null</c>, and the caller keeps the URL-only
    /// OAuth config. <see cref="RegenerateAsync"/> always mints and overwrites the cache entry.</para>
    ///
    /// <para>Typical engine use:
    /// <code>
    /// var key = await ProjectKeyProvider.FromMachineCredentials().GetOrMintAsync(settings.ProjectPin, "unity", Environment.MachineName, projectRoot);
    /// var cloud = settings.WithProjectKey(key);
    /// configurator.GetHttpConfig(cloud, credentialMode: cloud.ResolveHttpCredentialMode()).Configure();
    /// </code></para>
    /// </summary>
    public sealed class ProjectKeyProvider
    {
        /// <summary>The hosted issuer (Cloud mode).</summary>
        public const string DefaultIssuer = "https://ai-game.dev";

        /// <summary>Mint endpoint path, relative to the issuer origin (contract §2).</summary>
        public const string MintPath = "/api/mcp/project-keys";

        /// <summary>Validation endpoint path, relative to the issuer origin (contract §2).</summary>
        public const string CurrentPath = "/api/mcp/project-keys/current";

        /// <summary>Engine ids the server accepts (contract §2); anything else is sent as <c>unknown</c>.</summary>
        public static readonly string[] KnownEngines = { "unity", "godot", "unreal", "unknown" };

        private static readonly HttpClient SharedHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        private readonly Func<CancellationToken, Task<string?>> _accessTokenProvider;
        private readonly Func<string?>? _subjectFallback;
        private readonly HttpClient _http;
        private readonly ILogger? _logger;

        // Single-flight: concurrent get-or-mint / regenerate calls on one provider (e.g. "configure every agent")
        // must not each miss the cache and mint their own key.
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        /// <param name="accessTokenProvider">Returns the signed-in machine account's MCP-plane OAuth access
        /// token (agent or plugin family), or <c>null</c> when signed out.</param>
        /// <param name="issuer">Issuer / server base URL; normalised to its origin.</param>
        /// <param name="store">The local cache; defaults to <c>~/.ai-game-dev/project-keys.json</c>.</param>
        /// <param name="httpClient">Optional client (tests inject a fake handler).</param>
        /// <param name="subjectFallback">The signed-in account's <c>sub</c> as the credential records it; preferred
        /// over the access token's own <c>sub</c> claim (contract §6).</param>
        /// <param name="logger">Optional diagnostics sink. Never receives a secret.</param>
        public ProjectKeyProvider(
            Func<CancellationToken, Task<string?>> accessTokenProvider,
            string issuer = DefaultIssuer,
            ProjectKeyStore? store = null,
            HttpClient? httpClient = null,
            Func<string?>? subjectFallback = null,
            ILogger? logger = null)
        {
            _accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));
            Issuer = ProjectKeyStore.NormalizeIssuerOrigin(issuer);
            Store = store ?? new ProjectKeyStore();
            _http = httpClient ?? SharedHttpClient;
            _subjectFallback = subjectFallback;
            _logger = logger;
        }

        /// <summary>The normalised issuer origin every request and cache entry is keyed on.</summary>
        public string Issuer { get; }

        /// <summary>The local cache this provider reads and writes.</summary>
        public ProjectKeyStore Store { get; }

        /// <summary>
        /// A provider backed by an in-process <see cref="com.IvanMurzak.McpPlugin.PluginCredentialProvider"/>
        /// (proactively refreshed plugin-family token) — the preferred wiring inside a running engine plugin.
        /// </summary>
        public static ProjectKeyProvider FromPluginCredentialProvider(
            com.IvanMurzak.McpPlugin.PluginCredentialProvider credentials,
            string issuer = DefaultIssuer,
            ProjectKeyStore? store = null,
            HttpClient? httpClient = null,
            ILogger? logger = null)
        {
            if (credentials == null)
                throw new ArgumentNullException(nameof(credentials));
            return new ProjectKeyProvider(
                ct => credentials.GetAccessTokenAsync(ct), issuer, store, httpClient, () => credentials.Subject, logger);
        }

        /// <summary>
        /// A provider that reads the machine credential store (<c>~/.ai-game-dev/credentials.json</c>)
        /// directly: the first unexpired access token among the plugin, agent and legacy families. It does
        /// not refresh — an expired login yields <c>null</c> (fall back to the OAuth config) until the engine
        /// or the app refreshes it.
        /// </summary>
        /// <param name="issuer">Issuer override; when <c>null</c>, the origin of the stored credential's
        /// <c>serverTarget</c> (contract §2 — a local-stack login never mints against production), else
        /// <see cref="DefaultIssuer"/>.</param>
        public static ProjectKeyProvider FromMachineCredentials(
            string? issuer = null,
            MachineCredentialStore? credentialStore = null,
            ProjectKeyStore? store = null,
            HttpClient? httpClient = null,
            ILogger? logger = null)
        {
            var credentials = credentialStore ?? new MachineCredentialStore();
            issuer ??= IssuerFromServerTarget(credentials.Read()?.ServerTarget);
            string? subject = null;
            return new ProjectKeyProvider(
                _ =>
                {
                    var read = credentials.Read();
                    // Contract §6 account identity: credential subject, else the agent-family token's sub, else
                    // the plugin-family token's (a plugin-only machine must still reuse its cached key).
                    subject = read?.Subject
                        ?? TryGetJwtSubject(read?.Families?.Agent?.AccessToken)
                        ?? TryGetJwtSubject(read?.Families?.Plugin?.AccessToken);
                    return Task.FromResult(SelectUsableAccessToken(read, DateTimeOffset.UtcNow));
                },
                issuer, store ?? new ProjectKeyStore(credentials.BaseDirectory), httpClient, () => subject, logger);
        }

        /// <summary>
        /// Returns a valid project key for <paramref name="pin"/> (reusing the cached one when it is still
        /// valid for the signed-in account, else minting), or <c>null</c> when no key can be obtained (not
        /// signed in, auth rejected, server unreachable while nothing usable is cached).
        /// </summary>
        /// <param name="pin">The v2 routing pin (8 hex chars) — the key is strictly bound to it.</param>
        /// <param name="engine"><c>unity</c> | <c>godot</c> | <c>unreal</c> (anything else is sent as <c>unknown</c>).</param>
        /// <param name="machineName">Display name of this machine (e.g. <see cref="Environment.MachineName"/>).</param>
        /// <param name="label">Optional display label, e.g. the project folder path.</param>
        public async Task<string?> GetOrMintAsync(
            string pin, string engine, string machineName, string? label = null, CancellationToken cancellationToken = default)
        {
            pin = ProjectKeyStore.NormalizePin(pin);
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var accessToken = await _accessTokenProvider(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(accessToken))
                    return null; // no machine login: cannot mint, and a cached key cannot be attributed to anyone.
                if (IsCacheUnreadable())
                    return null;

                var sub = ResolveSubject(accessToken!);
                var cached = Store.Get(Issuer, pin);
                if (cached != null && sub != null && string.Equals(cached.Sub, sub, StringComparison.Ordinal))
                {
                    var validity = await ValidateAsync(cached.Key, pin, cancellationToken).ConfigureAwait(false);
                    if (validity != KeyValidity.Invalid)
                        return cached.Key; // valid, or unverifiable right now (transient) — reuse (contract §6).
                }

                var (key, _) = await MintAndStoreAsync(accessToken!, sub, pin, engine, machineName, label, cancellationToken,
                    keepConcurrentWrite: true, keyReadBeforeMint: cached?.Key).ConfigureAwait(false);
                return key;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// "Regenerate key": mints a fresh key for <paramref name="pin"/>, overwrites the cache entry, then
        /// revokes the previously cached key (<c>DELETE /api/mcp/project-keys/{keyId}</c>, contract §7) with the
        /// same access token. A failed revoke is logged and never fails the regenerate. Returns <c>null</c> when
        /// not signed in or the mint fails — the cached entry is then left untouched and nothing is revoked.
        /// </summary>
        public async Task<string?> RegenerateAsync(
            string pin, string engine, string machineName, string? label = null, CancellationToken cancellationToken = default)
        {
            pin = ProjectKeyStore.NormalizePin(pin);
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var accessToken = await _accessTokenProvider(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(accessToken) || IsCacheUnreadable())
                    return null;
                var previous = Store.Get(Issuer, pin);
                var (key, stored) = await MintAndStoreAsync(accessToken!, ResolveSubject(accessToken!), pin, engine, machineName, label, cancellationToken,
                    keepConcurrentWrite: false, keyReadBeforeMint: null).ConfigureAwait(false);
                // Revoke only once the new key is actually cached: a failed cache write leaves the old entry in
                // place, and revoking it then would strand the cache on a dead key.
                if (stored && previous is { KeyId: { Length: > 0 } oldKeyId })
                    await RevokeAsync(accessToken!, oldKeyId, pin, cancellationToken).ConfigureAwait(false);
                return key;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// An existing-but-unreadable cache is never overwritten (contract §6), so a key minted now could not be
        /// cached and every call would mint another one: fall back to the URL-only config and surface the error.
        /// </summary>
        private bool IsCacheUnreadable()
        {
            if (!Store.IsUnreadable)
                return false;
            _logger?.LogWarning("The project-key cache {Path} exists but cannot be read; it is left untouched and the URL-only config is used.", Store.FilePath);
            return true;
        }

        internal static string IssuerFromServerTarget(string? serverTarget)
        {
            if (string.IsNullOrWhiteSpace(serverTarget))
                return DefaultIssuer;
            try
            {
                return ProjectKeyStore.NormalizeIssuerOrigin(serverTarget!);
            }
            catch (ArgumentException)
            {
                return DefaultIssuer;
            }
        }

        // ── Internals ─────────────────────────────────────────────────────────────────────────────

        internal enum KeyValidity { Valid, Invalid, Unknown }

        private async Task<KeyValidity> ValidateAsync(string key, string pin, CancellationToken cancellationToken)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, Issuer + CurrentPath);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

                // Only 401 means revoked/unknown (contract §2/§6); every other failure is transient — reuse.
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    return KeyValidity.Invalid;

                if (!response.IsSuccessStatusCode)
                    return KeyValidity.Unknown;

                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var json = TryParseObject(body);
                if (json == null)
                    return KeyValidity.Unknown;
                if (json["active"] is JsonValue active && active.TryGetValue<bool>(out var isActive) && !isActive)
                    return KeyValidity.Invalid;
                var serverPin = ProjectKeyStore.GetString(json, "project_pin");
                if (serverPin != null && !string.Equals(serverPin, pin, StringComparison.OrdinalIgnoreCase))
                    return KeyValidity.Invalid; // cached under the wrong pin — never reuse a key for another project.
                return KeyValidity.Valid;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) || !cancellationToken.IsCancellationRequested)
            {
                _logger?.LogDebug("Project key validation could not reach {Issuer}: {Message}", Issuer, ex.Message);
                return KeyValidity.Unknown;
            }
        }

        private async Task RevokeAsync(string accessToken, string keyId, string pin, CancellationToken cancellationToken)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, Issuer + MintPath + "/" + Uri.EscapeDataString(keyId));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    _logger?.LogWarning("Revoking the previous project key {KeyId} for pin {Pin} was refused by {Issuer}: HTTP {Status}.", keyId, pin, Issuer, (int)response.StatusCode);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) || !cancellationToken.IsCancellationRequested)
            {
                _logger?.LogWarning("Revoking the previous project key {KeyId} for pin {Pin} could not reach {Issuer}: {Message}", keyId, pin, Issuer, ex.Message);
            }
        }

        /// <returns>The key to use (<c>null</c> when the mint failed) and whether it was written to the cache.</returns>
        private async Task<(string? Key, bool Stored)> MintAndStoreAsync(
            string accessToken, string? sub, string pin, string engine, string machineName, string? label,
            CancellationToken cancellationToken, bool keepConcurrentWrite, string? keyReadBeforeMint)
        {
            engine = NormalizeEngine(engine);
            var payload = new JsonObject
            {
                ["project_pin"] = pin,
                ["engine"] = engine,
                ["machine_name"] = Head(machineName ?? string.Empty),
            };
            if (!string.IsNullOrEmpty(label))
                payload["label"] = Tail(label!); // a folder path's tail is its distinguishing part (contract §2)

            string body;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, Issuer + MintPath)
                {
                    Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger?.LogWarning("Project key mint for pin {Pin} was refused by {Issuer}: HTTP {Status}.", pin, Issuer, (int)response.StatusCode);
                    return (null, false);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) || !cancellationToken.IsCancellationRequested)
            {
                _logger?.LogWarning("Project key mint for pin {Pin} could not reach {Issuer}: {Message}", pin, Issuer, ex.Message);
                return (null, false);
            }

            var json = TryParseObject(body);
            var key = json == null ? null : ProjectKeyStore.GetString(json, "key");
            if (string.IsNullOrEmpty(key))
            {
                _logger?.LogWarning("Project key mint for pin {Pin} returned no key.", pin);
                return (null, false);
            }

            try
            {
                var minted = new ProjectKeyEntry
                {
                    Key = key!,
                    KeyId = ProjectKeyStore.GetString(json!, "key_id"),
                    Pin = pin,
                    Issuer = Issuer,
                    Sub = sub,
                    Engine = engine,
                    CreatedAt = ProjectKeyStore.GetString(json!, "created_at"),
                };
                // Mint happened OUTSIDE the lock; a concurrent writer's fresh entry wins over ours (contract §6).
                key = keepConcurrentWrite ? Store.PutUnlessConcurrentlyReplaced(minted, keyReadBeforeMint) : minted.Key;
                if (!keepConcurrentWrite)
                    Store.Put(minted);
                return (key, true);
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException
                || ex is System.Security.Cryptography.CryptographicException)
            {
                // The key is valid either way; a cache write failure only costs a re-mint next time.
                _logger?.LogWarning("Project key cache write failed: {Message}", ex.Message);
                return (key, false);
            }
        }

        // Contract §6: the credential's recorded subject, else the access token's own sub claim.
        private string? ResolveSubject(string accessToken) => _subjectFallback?.Invoke() ?? TryGetJwtSubject(accessToken);

        /// <summary>The server stores <c>machine_name</c> / <c>label</c> in at most this many chars (contract §2).</summary>
        public const int MaxDisplayFieldLength = 120;

        private static string Head(string s) => s.Length <= MaxDisplayFieldLength ? s : s.Substring(0, MaxDisplayFieldLength);
        private static string Tail(string s) => s.Length <= MaxDisplayFieldLength ? s : s.Substring(s.Length - MaxDisplayFieldLength);

        internal static string NormalizeEngine(string? engine)
        {
            var e = (engine ?? string.Empty).Trim().ToLowerInvariant();
            return Array.IndexOf(KnownEngines, e) >= 0 ? e : "unknown";
        }

        /// <summary>The <c>sub</c> claim of a JWT (payload decoded WITHOUT verification — used only to tell
        /// which account a cached key belongs to), or <c>null</c> for an opaque token.</summary>
        internal static string? TryGetJwtSubject(string? token)
        {
            if (string.IsNullOrEmpty(token))
                return null;
            var parts = token!.Split('.');
            if (parts.Length != 3)
                return null;
            try
            {
                var b64 = parts[1].Replace('-', '+').Replace('_', '/');
                b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
                var payload = TryParseObject(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
                return payload == null ? null : ProjectKeyStore.GetString(payload, "sub");
            }
            catch (FormatException)
            {
                return null;
            }
        }

        internal static string? SelectUsableAccessToken(MachineCredentials? credentials, DateTimeOffset now)
        {
            var families = credentials?.Families;
            foreach (var family in new[] { families?.Plugin, families?.Agent, families?.Legacy })
            {
                if (string.IsNullOrEmpty(family?.AccessToken))
                    continue;
                if (family!.ExpiresAt is DateTimeOffset expires && expires <= now.AddSeconds(30))
                    continue;
                return family.AccessToken;
            }
            return null;
        }

        private static JsonObject? TryParseObject(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;
            try
            {
                return JsonNode.Parse(json!) as JsonObject;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
