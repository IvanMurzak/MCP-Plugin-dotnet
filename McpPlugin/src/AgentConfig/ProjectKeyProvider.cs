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

        /// <param name="accessTokenProvider">Returns the signed-in machine account's MCP-plane OAuth access
        /// token (agent or plugin family), or <c>null</c> when signed out.</param>
        /// <param name="issuer">Issuer / server base URL; normalised to its origin.</param>
        /// <param name="store">The local cache; defaults to <c>~/.ai-game-dev/project-keys.json</c>.</param>
        /// <param name="httpClient">Optional client (tests inject a fake handler).</param>
        /// <param name="subjectFallback">Account <c>sub</c> to use when the access token is not a JWT.</param>
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
        public static ProjectKeyProvider FromMachineCredentials(
            string issuer = DefaultIssuer,
            MachineCredentialStore? credentialStore = null,
            ProjectKeyStore? store = null,
            HttpClient? httpClient = null,
            ILogger? logger = null)
        {
            var credentials = credentialStore ?? new MachineCredentialStore();
            string? subject = null;
            return new ProjectKeyProvider(
                _ =>
                {
                    var read = credentials.Read();
                    subject = read?.Subject;
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
            var accessToken = await _accessTokenProvider(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(accessToken))
                return null; // no machine login: cannot mint, and a cached key cannot be attributed to anyone.

            var sub = ResolveSubject(accessToken!);
            var cached = Store.Get(Issuer, pin);
            if (cached != null && sub != null && string.Equals(cached.Sub, sub, StringComparison.Ordinal))
            {
                var validity = await ValidateAsync(cached.Key, pin, cancellationToken).ConfigureAwait(false);
                if (validity != KeyValidity.Invalid)
                    return cached.Key; // valid, or unverifiable right now (transient) — reuse (contract §6).
            }

            return await MintAndStoreAsync(accessToken!, sub, pin, engine, machineName, label, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// "Regenerate key": mints a fresh key for <paramref name="pin"/> and overwrites the cache entry
        /// (older keys stay valid server-side until revoked). Returns <c>null</c> when not signed in or the
        /// mint fails — the cached entry is then left untouched.
        /// </summary>
        public async Task<string?> RegenerateAsync(
            string pin, string engine, string machineName, string? label = null, CancellationToken cancellationToken = default)
        {
            pin = ProjectKeyStore.NormalizePin(pin);
            var accessToken = await _accessTokenProvider(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(accessToken))
                return null;
            return await MintAndStoreAsync(accessToken!, ResolveSubject(accessToken!), pin, engine, machineName, label, cancellationToken).ConfigureAwait(false);
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

                if (response.StatusCode == HttpStatusCode.Unauthorized
                    || response.StatusCode == HttpStatusCode.Forbidden
                    || response.StatusCode == HttpStatusCode.NotFound)
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

        private async Task<string?> MintAndStoreAsync(
            string accessToken, string? sub, string pin, string engine, string machineName, string? label,
            CancellationToken cancellationToken)
        {
            engine = NormalizeEngine(engine);
            var payload = new JsonObject
            {
                ["project_pin"] = pin,
                ["engine"] = engine,
                ["machine_name"] = machineName ?? string.Empty,
            };
            if (!string.IsNullOrEmpty(label))
                payload["label"] = label;

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
                    return null;
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) || !cancellationToken.IsCancellationRequested)
            {
                _logger?.LogWarning("Project key mint for pin {Pin} could not reach {Issuer}: {Message}", pin, Issuer, ex.Message);
                return null;
            }

            var json = TryParseObject(body);
            var key = json == null ? null : ProjectKeyStore.GetString(json, "key");
            if (string.IsNullOrEmpty(key))
            {
                _logger?.LogWarning("Project key mint for pin {Pin} returned no key.", pin);
                return null;
            }

            try
            {
                Store.Put(new ProjectKeyEntry
                {
                    Key = key!,
                    KeyId = ProjectKeyStore.GetString(json!, "key_id"),
                    Pin = pin,
                    Issuer = Issuer,
                    Sub = sub,
                    Engine = engine,
                    CreatedAt = ProjectKeyStore.GetString(json!, "created_at"),
                });
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                // The key is valid either way; a cache write failure only costs a re-mint next time.
                _logger?.LogWarning("Project key cache write failed: {Message}", ex.Message);
            }
            return key;
        }

        private string? ResolveSubject(string accessToken) => TryGetJwtSubject(accessToken) ?? _subjectFallback?.Invoke();

        internal static string NormalizeEngine(string? engine)
        {
            var e = (engine ?? string.Empty).Trim().ToLowerInvariant();
            return Array.IndexOf(KnownEngines, e) >= 0 ? e : "unknown";
        }

        /// <summary>The <c>sub</c> claim of a JWT (payload decoded WITHOUT verification — used only to tell
        /// which account a cached key belongs to), or <c>null</c> for an opaque token.</summary>
        internal static string? TryGetJwtSubject(string token)
        {
            var parts = token.Split('.');
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
