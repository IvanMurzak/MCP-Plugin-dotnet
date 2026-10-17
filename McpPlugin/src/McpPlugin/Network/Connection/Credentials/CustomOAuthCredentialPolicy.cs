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
using System.Threading.Tasks;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// The bearer rule for a <b>Custom</b> connection in <c>oauth</c> auth mode, identical in every engine plugin.
    /// The stored ai-game.dev account credential (the machine store's access token) is sent only to a loopback host
    /// (<see cref="HostScope.IsLoopback"/>). Any other host gets the explicitly configured token, or the connection
    /// fails with <see cref="CredentialScopeException"/>. Cloud mode is out of scope: there the cloud URL is the
    /// token's own audience.
    /// </summary>
    public static class CustomOAuthCredentialPolicy
    {
        /// <summary>
        /// Resolve the bearer for a Custom+oauth connection to <paramref name="host"/>.
        /// <list type="bullet">
        ///   <item>Loopback host: the account token from <paramref name="accountToken"/>; when it yields nothing,
        ///   <paramref name="explicitToken"/>; otherwise <c>null</c> (anonymous).</item>
        ///   <item>Any other host: <paramref name="explicitToken"/>; <paramref name="accountToken"/> is never invoked.
        ///   Without an explicit token this throws <see cref="CredentialScopeException"/>.</item>
        /// </list>
        /// </summary>
        /// <param name="host">The Custom server URL the connection dials.</param>
        /// <param name="accountToken">Reads the stored account access token (may return null when signed out).</param>
        /// <param name="explicitToken">The user-configured bearer (environment variable or settings), if any.</param>
        /// <param name="tokenSettingName">What the user sets to supply a token, e.g. <c>UNITY_MCP_TOKEN</c>; used in the error.</param>
        public static async Task<string?> ResolveAsync(
            string? host,
            Func<Task<string?>>? accountToken,
            string? explicitToken,
            string tokenSettingName)
        {
            var hasExplicit = !string.IsNullOrEmpty(explicitToken);
            if (!HostScope.IsLoopback(host))
            {
                if (hasExplicit)
                    return explicitToken;
                throw new CredentialScopeException(host, tokenSettingName);
            }

            if (accountToken != null)
            {
                var token = await accountToken().ConfigureAwait(false);
                if (!string.IsNullOrEmpty(token))
                    return token;
            }
            return hasExplicit ? explicitToken : null;
        }
    }

    /// <summary>
    /// Thrown from a connection's credential provider when the ai-game.dev account token would be sent to a
    /// non-loopback Custom host and no explicit token is configured. The connection attempt fails with this message.
    /// </summary>
    public sealed class CredentialScopeException : InvalidOperationException
    {
        /// <summary>The refused host as configured (may be null or malformed).</summary>
        public string? Host { get; }

        /// <summary>Creates the refusal for <paramref name="host"/>; <paramref name="tokenSettingName"/> names the token setting.</summary>
        public CredentialScopeException(string? host, string tokenSettingName)
            : base(BuildMessage(host, tokenSettingName))
        {
            Host = host;
        }

        /// <summary>The actionable refusal text.</summary>
        public static string BuildMessage(string? host, string tokenSettingName)
            => $"Refusing to send your ai-game.dev account token to {DescribeHost(host)}; set {tokenSettingName} or use a localhost server.";

        static string DescribeHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return "an empty host";
            // Show the authority only: never echo user info or a path/query that could carry a secret.
            return Uri.TryCreate(host!.Trim(), UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
                ? uri.Host + (uri.IsDefaultPort ? string.Empty : ":" + uri.Port)
                : "a non-loopback host";
        }
    }
}
