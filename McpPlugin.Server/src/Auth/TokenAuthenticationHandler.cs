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
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Server.Auth.OAuth;
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
        public const string SubjectClaimType = "sub";
        public const string ScopeClaimType = "scope";
        public const string ClientIdClaimType = "client_id";

        readonly IAuthorizationWebhookService _authorizationWebhookService;
        readonly IOAuthTokenValidator? _oauthValidator;
        readonly OAuthResourceServerConfig? _oauthConfig;

        public TokenAuthenticationHandler(
            IOptionsMonitor<TokenAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IAuthorizationWebhookService authorizationWebhookService,
            IOAuthTokenValidator? oauthValidator = null,
            OAuthResourceServerConfig? oauthConfig = null)
            : base(options, logger, encoder)
        {
            _authorizationWebhookService = authorizationWebhookService ?? throw new ArgumentNullException(nameof(authorizationWebhookService));
            _oauthValidator = oauthValidator;
            _oauthConfig = oauthConfig;
        }

        protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = 401;

            if (Options.OAuthMode && _oauthConfig != null)
            {
                // MCP OAuth resource-server challenge (RFC 9728): the client MUST be able to discover
                // the authorization server from the ABSOLUTE resource_metadata URL, plus the scope.
                var metadataUrl = _oauthConfig.ProtectedResourceMetadataUrl();
                Response.Headers.WWWAuthenticate =
                    $"Bearer resource_metadata=\"{metadataUrl}\", scope=\"{OAuthResourceServerConfig.AgentScope}\"";
                await Response.WriteAsJsonAsync(new
                {
                    error = "invalid_token",
                    error_description = "Authentication required. Discover the authorization server via the resource_metadata URL and present a Bearer access token.",
                    resource_metadata = metadataUrl,
                    scope = OAuthResourceServerConfig.AgentScope
                });
                return;
            }

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
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

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            return Options.OAuthMode
                ? HandleOAuthAuthenticateAsync()
                : HandleLegacyAuthenticateAsync();
        }

        // ── OAuth resource-server path (mcp-authorize b2) ─────────────────────────────────────────
        async Task<AuthenticateResult> HandleOAuthAuthenticateAsync()
        {
            if (!TryGetBearerToken(out var token))
            {
                // No/empty/non-Bearer credential. On the hub path stay silent (BaseHub does its own
                // auth). Elsewhere NoResult lets the RequireAuthorization pipeline issue the 401
                // resource_metadata challenge.
                return AuthenticateResult.NoResult();
            }

            if (_oauthValidator == null)
            {
                Logger.LogError("OAuth mode is enabled but no token validator is configured.");
                return IsHubPath() ? AuthenticateResult.NoResult() : AuthenticateResult.Fail("OAuth validator not configured.");
            }

            var validation = await _oauthValidator.ValidateAsync(token, Context.RequestAborted);
            if (!validation.Succeeded)
            {
                if (!IsHubPath())
                {
                    Logger.LogWarning(
                        "MCP OAuth auth rejected: {Reason}. RemoteIp: {RemoteIpAddress}, UserAgent: {UserAgent}, RequestPath: {RequestPath}, TokenFingerprint: {TokenFingerprint}",
                        validation.FailureReason, GetClientIpAddress(), Request.Headers.UserAgent.ToString(),
                        Request.Path.Value, FingerprintToken(token));
                }
                return IsHubPath()
                    ? AuthenticateResult.NoResult()
                    : AuthenticateResult.Fail($"Invalid token: {validation.FailureReason}");
            }

            // Optional enforcement channel (hosted account-status revocation). NoOp locally.
            if (!await AuthorizeAiAgentAsync(token))
                return AuthenticateResult.Fail("Authorization webhook rejected the connection.");

            var claims = new List<Claim> { new Claim(TokenClaimType, token) };
            if (!string.IsNullOrEmpty(validation.Subject))
            {
                claims.Add(new Claim(SubjectClaimType, validation.Subject!));
                claims.Add(new Claim(ClaimTypes.NameIdentifier, validation.Subject!));
            }
            if (!string.IsNullOrEmpty(validation.Scope))
                claims.Add(new Claim(ScopeClaimType, validation.Scope!));
            if (!string.IsNullOrEmpty(validation.ClientId))
                claims.Add(new Claim(ClientIdClaimType, validation.ClientId!));

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
        }

        // ── Legacy token path (auth=required / none) — unchanged behavior ─────────────────────────
        async Task<AuthenticateResult> HandleLegacyAuthenticateAsync()
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
            {
                if (!IsHubPath())
                {
                    Logger.LogWarning(
                        "MCP auth rejected: Empty Bearer token. ConnectionId: {ConnectionId}, RemoteIp: {RemoteIpAddress}, UserAgent: {UserAgent}, RequestPath: {RequestPath}",
                        Context.TraceIdentifier, GetClientIpAddress(), Request.Headers.UserAgent.ToString(), Request.Path.Value);
                }
                return IsHubPath() ? AuthenticateResult.NoResult() : AuthenticateResult.Fail("Empty Bearer token.");
            }

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

            // No tier matched — unrecognized token.
            //
            // On the SignalR hub path (Consts.Hub.RemoteApp == "/hub/mcp-server") this happens
            // for EVERY new plugin connection: the plugin's bearer token is registered with
            // ClientUtils.AddClient only AFTER OnConnectedAsync runs, but middleware-level
            // authentication runs BEFORE OnConnectedAsync — chicken-and-egg. The hub endpoint
            // is not RequireAuthorization()-gated, so the connection still proceeds; but
            // returning Fail here causes the framework to auto-emit a "[7] McpPluginToken was
            // not authenticated" info log on every probe — a documented production-log noise
            // source (issue #99). Returning NoResult on the hub path lets the connection
            // through silently while preserving the Fail+401 challenge on every other path
            // (which IS RequireAuthorization()-gated). The same hub-path-silencing rule is
            // also applied to the empty-Bearer case above.
            if (ticket == null)
            {
                if (!IsHubPath())
                {
                    Logger.LogWarning(
                        "MCP auth rejected: Invalid or unrecognized token. ConnectionId: {ConnectionId}, RemoteIp: {RemoteIpAddress}, UserAgent: {UserAgent}, RequestPath: {RequestPath}, TokenFingerprint: {TokenFingerprint}",
                        Context.TraceIdentifier, GetClientIpAddress(), Request.Headers.UserAgent.ToString(),
                        Request.Path.Value, FingerprintToken(token));
                }
                return IsHubPath() ? AuthenticateResult.NoResult() : AuthenticateResult.Fail("Invalid or unrecognized token.");
            }

            // Single authorization webhook check for whichever tier matched.
            // Note: AuthorizeAiAgentAsync returns false for both explicit denials
            // and webhook failures (timeout/network error) when fail-open is disabled.
            if (!await AuthorizeAiAgentAsync(token))
                return AuthenticateResult.Fail("Authorization webhook rejected the connection.");

            return AuthenticateResult.Success(ticket);
        }

        bool TryGetBearerToken(out string token)
        {
            token = string.Empty;
            var authHeader = Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return false;
            token = authHeader.Substring("Bearer ".Length).Trim();
            return token.Length > 0;
        }

        // True when the request targets the SignalR hub endpoint. Used to silence the
        // "[7] McpPluginToken was not authenticated" info log noise on hub probes — see
        // the comment on the no-tier-matched branch in HandleLegacyAuthenticateAsync.
        bool IsHubPath()
        {
            var path = Request.Path.Value;
            return !string.IsNullOrEmpty(path)
                && path.StartsWith(Consts.Hub.RemoteApp, StringComparison.OrdinalIgnoreCase);
        }

        Task<bool> AuthorizeAiAgentAsync(string token)
        {
            return _authorizationWebhookService.AuthorizeAiAgentAsync(
                connectionId: Context.TraceIdentifier,
                bearerToken: token,
                remoteIpAddress: GetClientIpAddress(),
                userAgent: Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null,
                requestPath: Request.Path.Value,
                cancellationToken: Context.RequestAborted);
        }

        string? GetClientIpAddress()
        {
            var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwardedFor))
                return forwardedFor.Split(',')[0].Trim();

            var realIp = Request.Headers["X-Real-IP"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(realIp))
                return realIp.Trim();

            return Request.HttpContext.Connection.RemoteIpAddress?.ToString();
        }

        static string? FingerprintToken(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim()));
            return Convert.ToHexString(bytes).Substring(0, 16).ToLowerInvariant();
        }
    }

    public class TokenAuthenticationOptions : AuthenticationSchemeOptions
    {
        public string? ServerToken { get; set; }
        public bool RequireToken { get; set; }

        /// <summary>
        /// When true, the handler runs the OAuth resource-server validation path (ES256/JWKS +
        /// introspection) instead of the legacy token-equality path (mcp-authorize b2).
        /// </summary>
        public bool OAuthMode { get; set; }
    }
}
