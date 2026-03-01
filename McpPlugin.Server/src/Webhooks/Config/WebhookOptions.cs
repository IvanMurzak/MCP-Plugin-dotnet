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

        public bool IsEnabled => ToolWebhookUrl != null || PromptWebhookUrl != null || ResourceWebhookUrl != null || ConnectionWebhookUrl != null;
        public bool IsToolEnabled => ToolWebhookUrl != null;
        public bool IsPromptEnabled => PromptWebhookUrl != null;
        public bool IsResourceEnabled => ResourceWebhookUrl != null;
        public bool IsConnectionEnabled => ConnectionWebhookUrl != null;
        public bool HasToken => TokenValue != null;
        public bool HasInvalidUrls => _invalidUrls.Count > 0;

        readonly List<(string Category, string Url)> _invalidUrls = new List<(string, string)>();

        public WebhookOptions(
            string? toolWebhookUrl,
            string? promptWebhookUrl,
            string? resourceWebhookUrl,
            string? connectionWebhookUrl,
            string? tokenValue,
            string? headerName,
            int timeoutMs)
        {
            ToolWebhookUrl = ValidateUrl(toolWebhookUrl, "Tool");
            PromptWebhookUrl = ValidateUrl(promptWebhookUrl, "Prompt");
            ResourceWebhookUrl = ValidateUrl(resourceWebhookUrl, "Resource");
            ConnectionWebhookUrl = ValidateUrl(connectionWebhookUrl, "Connection");
            TokenValue = tokenValue;
            HeaderName = IsValidHeaderName(headerName) ? headerName! : DefaultHeaderName;
            TimeoutMs = timeoutMs > 0 ? timeoutMs : DefaultTimeoutMs;
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
                timeoutMs: dataArguments.WebhookTimeoutMs
            );
        }

        public void LogWarnings(ILogger logger)
        {
            foreach (var (category, url) in _invalidUrls)
            {
                logger.LogWarning("Invalid webhook URL for {Category}: '{Url}'. URL must be an absolute HTTP or HTTPS URL. Treating as unconfigured.",
                    category, url);
            }

            if (!IsEnabled)
                return;

            if (!HasToken)
                logger.LogWarning("Webhook URLs configured but no security token set. Webhooks will be sent without authentication.");

            CheckHttpWarning(logger, ToolWebhookUrl, "Tool");
            CheckHttpWarning(logger, PromptWebhookUrl, "Prompt");
            CheckHttpWarning(logger, ResourceWebhookUrl, "Resource");
            CheckHttpWarning(logger, ConnectionWebhookUrl, "Connection");
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
