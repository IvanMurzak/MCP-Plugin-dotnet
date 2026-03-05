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
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit;

namespace McpPlugin.Server.Tests.Webhooks
{
    [Collection("McpPlugin.Server")]
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

        // --- Authorization webhook ---

        [Fact]
        public void IsAuthorizationEnabled_WhenAuthorizationUrlSet_ReturnsTrue()
        {
            var options = new WebhookOptions(null, null, null, null, null, null, 10000,
                authorizationWebhookUrl: "https://example.com/auth");

            options.IsAuthorizationEnabled.ShouldBeTrue();
            options.AuthorizationWebhookUrl.ShouldBe("https://example.com/auth");
        }

        [Fact]
        public void IsAuthorizationEnabled_WhenAuthorizationUrlNull_ReturnsFalse()
        {
            var options = new WebhookOptions(null, null, null, null, null, null, 10000);

            options.IsAuthorizationEnabled.ShouldBeFalse();
            options.AuthorizationWebhookUrl.ShouldBeNull();
        }

        [Fact]
        public void IsEnabled_WhenOnlyAuthorizationUrlSet_ReturnsFalse()
        {
            // IsEnabled covers tool/prompt/resource/connection only — authorization is separate
            var options = new WebhookOptions(null, null, null, null, null, null, 10000,
                authorizationWebhookUrl: "https://example.com/auth");

            options.IsEnabled.ShouldBeFalse();
            options.IsAuthorizationEnabled.ShouldBeTrue();
        }

        [Fact]
        public void AuthorizationFailOpen_DefaultsFalse()
        {
            var options = new WebhookOptions(null, null, null, null, null, null, 10000);

            options.AuthorizationFailOpen.ShouldBeFalse();
        }

        [Fact]
        public void AuthorizationFailOpen_WhenSetToTrue_ReturnsTrue()
        {
            var options = new WebhookOptions(null, null, null, null, null, null, 10000,
                authorizationWebhookUrl: "https://example.com/auth",
                authorizationFailOpen: true);

            options.AuthorizationFailOpen.ShouldBeTrue();
        }

        [Fact]
        public void AuthorizationUrl_WhenInvalid_TreatedAsNullAndTracked()
        {
            var options = new WebhookOptions(null, null, null, null, null, null, 10000,
                authorizationWebhookUrl: "not-a-url");

            options.AuthorizationWebhookUrl.ShouldBeNull();
            options.IsAuthorizationEnabled.ShouldBeFalse();
            options.HasInvalidUrls.ShouldBeTrue();
        }

        [Fact]
        public void AuthorizationUrl_WhenFtpScheme_TreatedAsNull()
        {
            var options = new WebhookOptions(null, null, null, null, null, null, 10000,
                authorizationWebhookUrl: "ftp://example.com/auth");

            options.AuthorizationWebhookUrl.ShouldBeNull();
            options.IsAuthorizationEnabled.ShouldBeFalse();
        }

        [Fact]
        public void FromDataArguments_ParsesAuthorizationUrl()
        {
            var args = new DataArguments(new[]
            {
                "webhook-authorization-url=https://example.com/auth",
                "webhook-token=secret"
            });

            var options = WebhookOptions.FromDataArguments(args);

            options.AuthorizationWebhookUrl.ShouldBe("https://example.com/auth");
            options.IsAuthorizationEnabled.ShouldBeTrue();
            options.AuthorizationFailOpen.ShouldBeFalse();
        }

        [Fact]
        public void FromDataArguments_ParsesAuthorizationFailOpen()
        {
            var args = new DataArguments(new[]
            {
                "webhook-authorization-url=https://example.com/auth",
                "webhook-authorization-fail-open=true"
            });

            var options = WebhookOptions.FromDataArguments(args);

            options.AuthorizationFailOpen.ShouldBeTrue();
        }

        [Fact]
        public void FromDataArguments_DefaultsAuthorizationWhenNotProvided()
        {
            var args = new DataArguments(System.Array.Empty<string>());
            var options = WebhookOptions.FromDataArguments(args);

            options.AuthorizationWebhookUrl.ShouldBeNull();
            options.IsAuthorizationEnabled.ShouldBeFalse();
            options.AuthorizationFailOpen.ShouldBeFalse();
        }

        // --- LogWarnings with authorization ---

        [Fact]
        public void LogWarnings_WhenNothingEnabled_DoesNotLogTokenWarning()
        {
            var options = new WebhookOptions(null, null, null, null, null, null, 10000);
            var mockLogger = new Mock<ILogger>();

            options.LogWarnings(mockLogger.Object);

            mockLogger.Verify(l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Never);
        }

        [Fact]
        public void LogWarnings_WhenOnlyAuthorizationEnabled_NoToken_LogsTokenWarning()
        {
            var options = new WebhookOptions(null, null, null, null, tokenValue: null, null, 10000,
                authorizationWebhookUrl: "https://example.com/auth");
            var mockLogger = new Mock<ILogger>();

            options.LogWarnings(mockLogger.Object);

            mockLogger.Verify(l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("no security token")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void LogWarnings_WhenOnlyAuthorizationEnabled_WithToken_NoTokenWarning()
        {
            var options = new WebhookOptions(null, null, null, null, tokenValue: "secret", null, 10000,
                authorizationWebhookUrl: "https://example.com/auth");
            var mockLogger = new Mock<ILogger>();

            options.LogWarnings(mockLogger.Object);

            mockLogger.Verify(l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("no security token")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Never);
        }

        [Fact]
        public void LogWarnings_WhenAuthorizationUrlIsHttp_LogsHttpWarning()
        {
            var options = new WebhookOptions(null, null, null, null, null, null, 10000,
                authorizationWebhookUrl: "http://example.com/auth");
            var mockLogger = new Mock<ILogger>();

            options.LogWarnings(mockLogger.Object);

            mockLogger.Verify(l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Authorization") && v.ToString()!.Contains("HTTP")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void LogWarnings_WhenAuthorizationUrlInvalid_LogsInvalidUrlWarning()
        {
            var options = new WebhookOptions(null, null, null, null, null, null, 10000,
                authorizationWebhookUrl: "not-a-url");
            var mockLogger = new Mock<ILogger>();

            options.LogWarnings(mockLogger.Object);

            mockLogger.Verify(l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Authorization") && v.ToString()!.Contains("not-a-url")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
    }
}
