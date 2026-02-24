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
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace com.IvanMurzak.McpPlugin.Server.Auth
{
    public class TokenAuthenticationHandler : AuthenticationHandler<TokenAuthenticationOptions>
    {
        public const string SchemeName = "McpPluginToken";
        public const string TokenClaimType = "mcp_plugin_token";

        public TokenAuthenticationHandler(
            IOptionsMonitor<TokenAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = 401;
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            Response.Headers.WWWAuthenticate = $"Bearer realm=\"MCP Plugin Server\", resource=\"{baseUrl}\"";
            return Task.CompletedTask;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // If no plugins have connected with tokens and no server token is configured,
            // allow anonymous access for backward compatibility
            if (!Options.RequireToken)
                return Task.FromResult(AuthenticateResult.NoResult());

            var authHeader = Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(AuthenticateResult.NoResult());

            var token = authHeader.Substring("Bearer ".Length).Trim();
            if (string.IsNullOrEmpty(token))
                return Task.FromResult(AuthenticateResult.Fail("Empty Bearer token."));

            // Token resolution — three additive sources, checked in priority order:
            //
            //   1. Plugin connection token  — a Bearer token that a connected .NET plugin
            //      registered via SignalR (AddClient). Maps token → SignalR connectionId.
            //      Grants a "connection_id" claim so the request can be routed to that plugin.
            //      Checked first because plugin connections are the primary auth mechanism.
            //
            //   2. DCR access token (RFC 7591) — a token issued by the /token endpoint after
            //      Dynamic Client Registration. Grants a "client_id" claim.
            //      Checked before ServerToken because DCR tokens are per-client and dynamic;
            //      a DCR token should never be confused with the static ServerToken.
            //
            //   3. ServerToken (static fallback) — a pre-shared token set at server startup
            //      via the `token` CLI argument / MCP_PLUGIN_TOKEN env var.
            //      Checked last; acts as a shared secret for deployments that don't use DCR.
            //
            // All three sources are additive: any valid token from any source is accepted.
            // The first successful match wins.

            // Tier 1 — plugin connection token
            var connectionId = ClientUtils.GetConnectionIdByToken(token);
            if (connectionId != null)
            {
                var claims = new[]
                {
                    new Claim(TokenClaimType, token),
                    new Claim("connection_id", connectionId)
                };
                var identity = new ClaimsIdentity(claims, SchemeName);
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, SchemeName);
                return Task.FromResult(AuthenticateResult.Success(ticket));
            }

            // Tier 2 — DCR access token (RFC 7591)
            var registeredClientId = ClientRegistrationStore.TryGetClientIdByAccessToken(token);
            if (registeredClientId != null)
            {
                var registeredClaims = new[]
                {
                    new Claim(TokenClaimType, token),
                    new Claim("client_id", registeredClientId)
                };
                var registeredIdentity = new ClaimsIdentity(registeredClaims, SchemeName);
                var registeredPrincipal = new ClaimsPrincipal(registeredIdentity);
                var registeredTicket = new AuthenticationTicket(registeredPrincipal, SchemeName);
                return Task.FromResult(AuthenticateResult.Success(registeredTicket));
            }

            // Tier 3 — static ServerToken fallback
            if (!string.IsNullOrEmpty(Options.ServerToken) && string.Equals(token, Options.ServerToken, StringComparison.Ordinal))
            {
                var claims = new[]
                {
                    new Claim(TokenClaimType, token)
                };
                var identity = new ClaimsIdentity(claims, SchemeName);
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, SchemeName);
                return Task.FromResult(AuthenticateResult.Success(ticket));
            }

            return Task.FromResult(AuthenticateResult.Fail("Invalid or unrecognized token."));
        }
    }

    public class TokenAuthenticationOptions : AuthenticationSchemeOptions
    {
        public string? ServerToken { get; set; }
        public bool RequireToken { get; set; }
    }
}
