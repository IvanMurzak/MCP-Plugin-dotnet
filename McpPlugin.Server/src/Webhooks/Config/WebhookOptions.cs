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
using com.IvanMurzak.McpPlugin.Common.Utils;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server.Webhooks
{
    public sealed class WebhookOptions
    {
        public const string DefaultHeaderName = "X-Webhook-Token";
        public const int DefaultTimeoutMs = 10000;

        public string? ToolWebhookUrl { get; }
        public string? PromptWebhookUrl { get; }
        public string? ResourceWebhookUrl { get; }
        public string? ConnectionWebhookUrl { get; }
        public string? TokenValue { get; }
        public string HeaderName { get; }
        public int TimeoutMs { get; }
        public string? AuthorizationWebhookUrl { get; }
        public bool AuthorizationFailOpen { get; }

        public bool IsEnabled => ToolWebhookUrl != null || PromptWebhookUrl != null || ResourceWebhookUrl != null || ConnectionWebhookUrl != null;
        public bool IsToolEnabled => ToolWebhookUrl != null;
        public bool IsPromptEnabled => PromptWebhookUrl != null;
        public bool IsResourceEnabled => ResourceWebhookUrl != null;
        public bool IsConnectionEnabled => ConnectionWebhookUrl != null;
        public bool HasToken => TokenValue != null;
        public bool HasInvalidUrls => _invalidUrls.Count > 0;
        public bool IsAuthorizationEnabled => AuthorizationWebhookUrl != null;
        public bool RequiresHttpClient => IsEnabled || IsAuthorizationEnabled;

        readonly List<(string Category, string Url)> _invalidUrls = new List<(string, string)>();

        public WebhookOptions(
            string? toolWebhookUrl = null,
            string? promptWebhookUrl = null,
            string? resourceWebhookUrl = null,
            string? connectionWebhookUrl = null,
            string? tokenValue = null,
            string? headerName = null,
            int timeoutMs = DefaultTimeoutMs,
            string? authorizationWebhookUrl = null,
            bool authorizationFailOpen = false)
        {
            ToolWebhookUrl = ValidateUrl(toolWebhookUrl, "Tool");
            PromptWebhookUrl = ValidateUrl(promptWebhookUrl, "Prompt");
            ResourceWebhookUrl = ValidateUrl(resourceWebhookUrl, "Resource");
            ConnectionWebhookUrl = ValidateUrl(connectionWebhookUrl, "Connection");
            TokenValue = tokenValue;
            HeaderName = IsValidHeaderName(headerName) ? headerName! : DefaultHeaderName;
            TimeoutMs = timeoutMs > 0 ? timeoutMs : DefaultTimeoutMs;
            AuthorizationWebhookUrl = ValidateUrl(authorizationWebhookUrl, "Authorization");
            AuthorizationFailOpen = authorizationFailOpen;
        }

        public static WebhookOptions FromDataArguments(IDataArguments dataArguments)
        {
            return new WebhookOptions(
                toolWebhookUrl: dataArguments.WebhookToolUrl,
                promptWebhookUrl: dataArguments.WebhookPromptUrl,
                resourceWebhookUrl: dataArguments.WebhookResourceUrl,
                connectionWebhookUrl: dataArguments.WebhookConnectionUrl,
                tokenValue: dataArguments.WebhookToken,
                headerName: dataArguments.WebhookHeader,
                timeoutMs: dataArguments.WebhookTimeoutMs,
                authorizationWebhookUrl: dataArguments.WebhookAuthorizationUrl,
                authorizationFailOpen: dataArguments.WebhookAuthorizationFailOpen
            );
        }

        public void LogWarnings(ILogger logger)
        {
            foreach (var (category, url) in _invalidUrls)
            {
                logger.LogWarning("Invalid webhook URL for {Category}: '{Url}'. URL must be an absolute HTTP or HTTPS URL. Treating as unconfigured.",
                    category, url);
            }

            if (!IsEnabled && !IsAuthorizationEnabled)
                return;

            if (!HasToken && (IsEnabled || IsAuthorizationEnabled))
                logger.LogWarning("Webhook URLs configured but no security token set. Webhooks will be sent without authentication. HMAC request signing will be disabled.");

            CheckHttpWarning(logger, ToolWebhookUrl, "Tool");
            CheckHttpWarning(logger, PromptWebhookUrl, "Prompt");
            CheckHttpWarning(logger, ResourceWebhookUrl, "Resource");
            CheckHttpWarning(logger, ConnectionWebhookUrl, "Connection");
            CheckHttpWarning(logger, AuthorizationWebhookUrl, "Authorization");

            if (AuthorizationFailOpen && IsAuthorizationEnabled)
                logger.LogWarning("Authorization webhook is configured with fail-open enabled. Connections will be allowed if the webhook is unreachable.");
        }

        static void CheckHttpWarning(ILogger logger, string? url, string category)
        {
            if (url != null && url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Webhook URL for {Category} uses HTTP (non-TLS). Security token will be transmitted without encryption: {Url}",
                    category, url);
            }
        }

        string? ValidateUrl(string? url, string category)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return url;
            }

            _invalidUrls.Add((category, url));
            return null;
        }

        static bool IsValidHeaderName(string? headerName)
        {
            if (string.IsNullOrWhiteSpace(headerName))
                return false;

            foreach (var c in headerName)
            {
                if (c <= 32 || c >= 127 || c == ':')
                    return false;
            }
            return true;
        }
    }
}
