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

namespace com.IvanMurzak.McpPlugin.Server.Auth.OAuth
{
    /// <summary>
    /// Resolved OAuth resource-server configuration (mcp-authorize b2), built from
    /// <c>--auth-issuer</c> (the AS) and <c>--public-url</c> (this RS's canonical resource id / the
    /// value a token's <c>aud</c> must contain). Server-side fetch endpoints are derived from the
    /// well-known conventions (RFC 8414 JWKS, RFC 7662 introspection) over a fetch base that defaults
    /// to the issuer but can be repointed via an optional <c>--auth-metadata-url</c> override
    /// (auth-fixes L2a / Gap B) — see <see cref="MetadataUrl"/>. The client-facing surface (the token
    /// <c>iss</c> check and the RFC 9728 PRM <c>authorization_servers</c>) always stays on
    /// <see cref="Issuer"/>.
    /// </summary>
    public sealed class OAuthResourceServerConfig
    {
        /// <summary>Default MCP agent scope advertised in 401 challenges (see 08-security.md).</summary>
        public const string AgentScope = "mcp:agent";

        /// <summary>Default clock-skew tolerance for <c>exp</c>/<c>nbf</c> (±5 min).</summary>
        public static readonly TimeSpan DefaultClockSkew = TimeSpan.FromMinutes(5);

        /// <summary>Authorization server identity, pinned as the token <c>iss</c> (e.g. <c>https://ai-game.dev</c>).</summary>
        public string Issuer { get; }

        /// <summary>This RS's canonical resource id — the value a token's <c>aud</c> must contain (the <c>--public-url</c>).</summary>
        public string ResourceUrl { get; }

        public TimeSpan ClockSkew { get; }

        /// <summary>
        /// The server-side metadata / fetch base URL — the base from which JWKS, introspection, and
        /// enrollment endpoints are derived. Defaults to <see cref="Issuer"/>; an explicit
        /// <c>--auth-metadata-url</c> / <c>MCP_AUTH_METADATA_URL</c> override (auth-fixes L2a / Gap B)
        /// repoints ONLY these server-side fetches, leaving the client-facing <c>iss</c> claim and PRM
        /// <c>authorization_servers</c> on <see cref="Issuer"/>. Used by a fully-local OAuth deployment
        /// where the client resolves the AS at a host address (e.g. <c>http://localhost</c>) that, from
        /// inside the RS container, would point back at the container itself.
        /// </summary>
        public string MetadataUrl { get; }

        /// <summary>The AS JWKS document URL (<c>{metadata-base}/.well-known/jwks.json</c>).</summary>
        public string JwksUri { get; }

        /// <summary>The AS token-introspection endpoint (<c>{metadata-base}/oauth/introspect</c>).</summary>
        public string IntrospectionEndpoint { get; }

        /// <summary>The AS account-enrollment endpoint (<c>{metadata-base}/api/auth/enroll/create</c>).</summary>
        public string EnrollmentEndpoint { get; }

        public OAuthResourceServerConfig(string issuer, string resourceUrl, TimeSpan? clockSkew = null, string? metadataUrl = null)
        {
            if (string.IsNullOrWhiteSpace(issuer))
                throw new ArgumentException("OAuth mode requires --auth-issuer (the authorization server URL).", nameof(issuer));
            if (string.IsNullOrWhiteSpace(resourceUrl))
                throw new ArgumentException("OAuth mode requires --public-url (this server's canonical resource id).", nameof(resourceUrl));

            Issuer = issuer.Trim().TrimEnd('/');
            ResourceUrl = resourceUrl.Trim();
            ClockSkew = clockSkew ?? DefaultClockSkew;

            // Server-side fetch base (auth-fixes L2a / Gap B). An empty/whitespace override falls back
            // to Issuer, so all of prod (which never sets it) is byte-identical to the pre-override
            // behavior. When set, ONLY the server-side fetch URLs below move to the override base —
            // Issuer and ProtectedResourceMetadataUrl()/PRM stay on the issuer (client-facing).
            MetadataUrl = string.IsNullOrWhiteSpace(metadataUrl)
                ? Issuer
                : metadataUrl!.Trim().TrimEnd('/');
            JwksUri = $"{MetadataUrl}/.well-known/jwks.json";
            IntrospectionEndpoint = $"{MetadataUrl}/oauth/introspect";
            EnrollmentEndpoint = $"{MetadataUrl}/api/auth/enroll/create";
        }

        /// <summary>
        /// The absolute Protected Resource Metadata URL (RFC 9728) for this resource, used as the
        /// <c>resource_metadata</c> challenge parameter. The well-known segment is inserted between
        /// authority and path (root-inserted) when the resource has a path (e.g.
        /// <c>https://ai-game.dev/mcp</c> → <c>https://ai-game.dev/.well-known/oauth-protected-resource/mcp</c>);
        /// a path-less resource simply appends it.
        /// </summary>
        public string ProtectedResourceMetadataUrl()
        {
            const string wellKnown = "/.well-known/oauth-protected-resource";
            if (!Uri.TryCreate(ResourceUrl, UriKind.Absolute, out var uri))
                return $"{ResourceUrl.TrimEnd('/')}{wellKnown}";

            var authority = uri.GetLeftPart(UriPartial.Authority);
            var path = uri.AbsolutePath;
            if (path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal))
                path = path.TrimEnd('/');

            return (string.IsNullOrEmpty(path) || path == "/")
                ? $"{authority}{wellKnown}"
                : $"{authority}{wellKnown}{path}";
        }
    }
}
