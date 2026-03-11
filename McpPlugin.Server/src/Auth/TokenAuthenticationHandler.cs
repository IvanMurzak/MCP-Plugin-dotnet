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
using com.IvanMurzak.McpPlugin.Server.Webhooks.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace com.IvanMurzak.McpPlugin.Server.Auth
{
    public class TokenAuthenticationHandler : AuthenticationHandler<TokenAuthenticationOptions>
    {
        public const string SchemeName = "McpPluginToken";
        public const string TokenClaimType = "mcp_plugin_token";

        readonly IAuthorizationWebhookService _authorizationWebhookService;

        public TokenAuthenticationHandler(
            IOptionsMonitor<TokenAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IAuthorizationWebhookService authorizationWebhookService)
            : base(options, logger, encoder)
        {
            _authorizationWebhookService = authorizationWebhookService ?? throw new ArgumentNullException(nameof(authorizationWebhookService));
        }

        protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            Response.StatusCode = 401;
            Response.Headers.WWWAuthenticate = $"Bearer realm=\"MCP Plugin Server\", resource=\"{baseUrl}\"";
            await Response.WriteAsJsonAsync(new
            {
                error = "Unauthorized",
                message = "A valid Bearer token is required. Provide it via the Authorization header: Bearer <token>"
            });
        }

        protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = 403;
            await Response.WriteAsJsonAsync(new
            {
                error = "Forbidden",
                message = "You do not have permission to access this resource."
            });
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // If no plugins have connected with tokens and no server token is configured,
            // allow anonymous access for backward compatibility
            if (!Options.RequireToken)
                return AuthenticateResult.NoResult();

            var authHeader = Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return AuthenticateResult.NoResult();

            var token = authHeader.Substring("Bearer ".Length).Trim();
            if (string.IsNullOrEmpty(token))
                return AuthenticateResult.Fail("Empty Bearer token.");

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
            AuthenticationTicket? ticket = null;
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
                ticket = new AuthenticationTicket(principal, SchemeName);
            }

            // Tier 2 — DCR access token (RFC 7591)
            if (ticket == null)
            {
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
                    ticket = new AuthenticationTicket(registeredPrincipal, SchemeName);
                }
            }

            // Tier 3 — static ServerToken fallback
            if (ticket == null
                && !string.IsNullOrEmpty(Options.ServerToken)
                && string.Equals(token, Options.ServerToken, StringComparison.Ordinal))
            {
                var claims = new[]
                {
                    new Claim(TokenClaimType, token)
                };
                var identity = new ClaimsIdentity(claims, SchemeName);
                var principal = new ClaimsPrincipal(identity);
                ticket = new AuthenticationTicket(principal, SchemeName);
            }

            // No tier matched — unrecognized token
            if (ticket == null)
                return AuthenticateResult.Fail("Invalid or unrecognized token.");

            // Single authorization webhook check for whichever tier matched
            if (!await AuthorizeAiAgentAsync(token))
                return AuthenticateResult.Fail("Authorization webhook denied the connection.");

            return AuthenticateResult.Success(ticket);
        }

        Task<bool> AuthorizeAiAgentAsync(string token)
        {
            return _authorizationWebhookService.AuthorizeAiAgentAsync(
                connectionId: Context.TraceIdentifier,
                bearerToken: token,
                remoteIpAddress: Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                userAgent: Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null,
                requestPath: Request.Path.Value,
                cancellationToken: Context.RequestAborted);
        }
    }

    public class TokenAuthenticationOptions : AuthenticationSchemeOptions
    {
        public string? ServerToken { get; set; }
        public bool RequireToken { get; set; }
    }
}
