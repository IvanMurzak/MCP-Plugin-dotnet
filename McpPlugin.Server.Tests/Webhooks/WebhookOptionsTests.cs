/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Webhooks;
using Shouldly;
using Xunit;

namespace McpPlugin.Server.Tests.Webhooks
{
    public class WebhookOptionsTests
    {
        [Fact]
        public void Defaults_WhenNoUrlsConfigured_IsNotEnabled()
        {
            var options = new WebhookOptions(null, null, null, null, null, null, 0);

            options.IsEnabled.ShouldBeFalse();
            options.IsToolEnabled.ShouldBeFalse();
            options.IsPromptEnabled.ShouldBeFalse();
            options.IsResourceEnabled.ShouldBeFalse();
            options.IsConnectionEnabled.ShouldBeFalse();
            options.HasToken.ShouldBeFalse();
            options.HeaderName.ShouldBe(WebhookOptions.DefaultHeaderName);
            options.TimeoutMs.ShouldBe(WebhookOptions.DefaultTimeoutMs);
        }

        [Fact]
        public void IsEnabled_WhenToolUrlConfigured_ReturnsTrue()
        {
            var options = new WebhookOptions("https://example.com/tools", null, null, null, null, null, 10000);

            options.IsEnabled.ShouldBeTrue();
            options.IsToolEnabled.ShouldBeTrue();
            options.IsPromptEnabled.ShouldBeFalse();
        }

        [Fact]
        public void IsEnabled_WhenConnectionUrlConfigured_ReturnsTrue()
        {
            var options = new WebhookOptions(null, null, null, "https://example.com/connections", null, null, 10000);

            options.IsEnabled.ShouldBeTrue();
            options.IsConnectionEnabled.ShouldBeTrue();
            options.IsToolEnabled.ShouldBeFalse();
        }

        [Fact]
        public void HasToken_WhenTokenProvided_ReturnsTrue()
        {
            var options = new WebhookOptions("https://example.com/tools", null, null, null, "my-token", null, 10000);

            options.HasToken.ShouldBeTrue();
            options.TokenValue.ShouldBe("my-token");
        }

        [Fact]
        public void HeaderName_WhenValid_UsesProvided()
        {
            var options = new WebhookOptions(null, null, null, null, null, "Authorization", 10000);

            options.HeaderName.ShouldBe("Authorization");
        }

        [Fact]
        public void HeaderName_WhenInvalid_UsesDefault()
        {
            var options = new WebhookOptions(null, null, null, null, null, "", 10000);

            options.HeaderName.ShouldBe(WebhookOptions.DefaultHeaderName);
        }

        [Fact]
        public void HeaderName_WhenNull_UsesDefault()
        {
            var options = new WebhookOptions(null, null, null, null, null, null, 10000);

            options.HeaderName.ShouldBe(WebhookOptions.DefaultHeaderName);
        }

        [Fact]
        public void TimeoutMs_WhenZeroOrNegative_UsesDefault()
        {
            var options = new WebhookOptions(null, null, null, null, null, null, 0);
            options.TimeoutMs.ShouldBe(WebhookOptions.DefaultTimeoutMs);

            var options2 = new WebhookOptions(null, null, null, null, null, null, -1);
            options2.TimeoutMs.ShouldBe(WebhookOptions.DefaultTimeoutMs);
        }

        [Fact]
        public void TimeoutMs_WhenPositive_UsesProvided()
        {
            var options = new WebhookOptions(null, null, null, null, null, null, 5000);
            options.TimeoutMs.ShouldBe(5000);
        }

        [Fact]
        public void Url_WhenInvalidFormat_TreatedAsNull()
        {
            var options = new WebhookOptions("not-a-url", null, null, null, null, null, 10000);

            options.ToolWebhookUrl.ShouldBeNull();
            options.IsToolEnabled.ShouldBeFalse();
            options.IsEnabled.ShouldBeFalse();
        }

        [Fact]
        public void Url_WhenFtpScheme_TreatedAsNull()
        {
            var options = new WebhookOptions("ftp://example.com/tools", null, null, null, null, null, 10000);

            options.ToolWebhookUrl.ShouldBeNull();
            options.IsEnabled.ShouldBeFalse();
        }

        [Fact]
        public void Url_WhenHttpScheme_IsAccepted()
        {
            var options = new WebhookOptions("http://localhost:9090/webhook", null, null, null, null, null, 10000);

            options.ToolWebhookUrl.ShouldBe("http://localhost:9090/webhook");
            options.IsToolEnabled.ShouldBeTrue();
        }

        [Fact]
        public void Url_WhenHttpsScheme_IsAccepted()
        {
            var options = new WebhookOptions("https://example.com/tools", null, null, null, null, null, 10000);

            options.ToolWebhookUrl.ShouldBe("https://example.com/tools");
            options.IsToolEnabled.ShouldBeTrue();
        }

        [Fact]
        public void FromDataArguments_ParsesCliArgs()
        {
            var args = new DataArguments(new[]
            {
                "webhook-tool-url=https://example.com/tools",
                "webhook-prompt-url=https://example.com/prompts",
                "webhook-token=secret",
                "webhook-header=X-API-Key",
                "webhook-timeout=5000"
            });

            var options = WebhookOptions.FromDataArguments(args);

            options.ToolWebhookUrl.ShouldBe("https://example.com/tools");
            options.PromptWebhookUrl.ShouldBe("https://example.com/prompts");
            options.ResourceWebhookUrl.ShouldBeNull();
            options.ConnectionWebhookUrl.ShouldBeNull();
            options.TokenValue.ShouldBe("secret");
            options.HeaderName.ShouldBe("X-API-Key");
            options.TimeoutMs.ShouldBe(5000);
        }

        [Fact]
        public void FromDataArguments_DefaultsWhenNotProvided()
        {
            var args = new DataArguments(System.Array.Empty<string>());
            var options = WebhookOptions.FromDataArguments(args);

            options.ToolWebhookUrl.ShouldBeNull();
            options.PromptWebhookUrl.ShouldBeNull();
            options.ResourceWebhookUrl.ShouldBeNull();
            options.ConnectionWebhookUrl.ShouldBeNull();
            options.TokenValue.ShouldBeNull();
            options.HeaderName.ShouldBe(WebhookOptions.DefaultHeaderName);
            options.TimeoutMs.ShouldBe(WebhookOptions.DefaultTimeoutMs);
            options.IsEnabled.ShouldBeFalse();
        }

        [Theory]
        [InlineData("X-Webhook-Token")]
        [InlineData("Authorization")]
        [InlineData("X-Custom-Header")]
        public void HeaderName_ValidNames_AreAccepted(string headerName)
        {
            var options = new WebhookOptions(null, null, null, null, null, headerName, 10000);
            options.HeaderName.ShouldBe(headerName);
        }

        [Theory]
        [InlineData("Invalid:Header")]
        [InlineData("")]
        [InlineData("  ")]
        public void HeaderName_InvalidNames_FallBackToDefault(string headerName)
        {
            var options = new WebhookOptions(null, null, null, null, null, headerName, 10000);
            options.HeaderName.ShouldBe(WebhookOptions.DefaultHeaderName);
        }
    }
}
