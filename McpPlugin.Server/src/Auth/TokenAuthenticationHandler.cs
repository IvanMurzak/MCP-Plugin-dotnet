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

            // Validate: token must match a registered plugin connection
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

            // Fallback: check against server-configured token
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
