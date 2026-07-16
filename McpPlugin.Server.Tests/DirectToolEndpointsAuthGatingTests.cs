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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Api;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Webhooks.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// 🔒 The direct-tool REST surface (<c>/api/tools</c>) and the system-tool surface (<c>/api/system-tools</c>)
    /// can EXECUTE tools, so they MUST fail closed in every credential-bearing mode — <c>oauth</c>, the offline
    /// <c>token</c> (mcp-authorize g6), and the deprecated <c>required</c> alias — and be reachable only in
    /// <c>none</c> mode. b7 closed the oauth gap (both once gated on the now-unreachable <c>required</c>); g6
    /// closes the token-mode gap so the REST surface is never an unauthenticated bypass of the endpoint's token gate.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class DirectToolEndpointsAuthGatingTests
    {
        const string Secret = "rest-gate-secret";

        [Theory]
        [InlineData("/api/tools")]
        [InlineData("/api/system-tools")]
        public async Task OauthMode_WithoutToken_Returns401(string route)
        {
            await using var host = await StartHostAsync(Consts.MCP.Server.AuthOption.oauth);
            using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };

            var response = await client.GetAsync(route);

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{route} must fail closed (401) in oauth mode without a token");
        }

        [Theory]
        [InlineData("/api/tools")]
        [InlineData("/api/system-tools")]
        public async Task TokenMode_WithoutToken_Returns401(string route)
        {
            await using var host = await StartHostAsync(Consts.MCP.Server.AuthOption.token);
            using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };

            var response = await client.GetAsync(route);

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{route} must fail closed (401) in token mode without a token");
        }

        [Theory]
        [InlineData("/api/tools")]
        [InlineData("/api/system-tools")]
        public async Task RequiredAliasMode_WithoutToken_Returns401(string route)
        {
            // The deprecated `required` alias resolves to token-gated behavior — it must gate the REST surface too.
            await using var host = await StartHostAsync(Consts.MCP.Server.AuthOption.required);
            using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };

            var response = await client.GetAsync(route);

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{route} must fail closed (401) in the required-alias mode without a token");
        }

        [Theory]
        [InlineData("/api/tools")]
        [InlineData("/api/system-tools")]
        public async Task TokenMode_WithValidToken_IsReachable(string route)
        {
            await using var host = await StartHostAsync(Consts.MCP.Server.AuthOption.token);
            using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };
            var request = new HttpRequestMessage(HttpMethod.Get, route);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {Secret}");

            var response = await client.SendAsync(request);

            response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized, $"{route} must be reachable with the correct token");
            ((int)response.StatusCode).ShouldBeLessThan(500, $"{route} should serve the (empty) tool list with a valid token");
        }

        [Theory]
        [InlineData("/api/tools")]
        [InlineData("/api/system-tools")]
        public async Task NoneMode_WithoutToken_IsReachable(string route)
        {
            await using var host = await StartHostAsync(Consts.MCP.Server.AuthOption.none);
            using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };

            var response = await client.GetAsync(route);

            response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized, $"{route} must be open (not 401) in none mode");
            ((int)response.StatusCode).ShouldBeLessThan(500, $"{route} should serve the (empty) tool list in none mode");
        }

        // ── Minimal in-memory host: only what the two endpoint mappers + the auth scheme need. ──

        static async Task<RunningHost> StartHostAsync(Consts.MCP.Server.AuthOption authOption)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();

            var dataArguments = Mock.Of<IDataArguments>(d => d.Authorization == authOption);
            builder.Services.AddSingleton(dataArguments);
            builder.Services.AddSingleton<IClientToolHub>(new StubToolHub());
            builder.Services.AddSingleton<IClientSystemToolHub>(new StubSystemToolHub());
            builder.Services.AddSingleton(Mock.Of<IAuthorizationWebhookService>());

            var localTokenMode = authOption == Consts.MCP.Server.AuthOption.token
                || authOption == Consts.MCP.Server.AuthOption.required;

            builder.Services
                .AddAuthentication(TokenAuthenticationHandler.SchemeName)
                .AddScheme<TokenAuthenticationOptions, TokenAuthenticationHandler>(
                    TokenAuthenticationHandler.SchemeName,
                    options =>
                    {
                        options.OAuthMode = authOption == Consts.MCP.Server.AuthOption.oauth;
                        options.LocalTokenMode = localTokenMode;
                        if (localTokenMode)
                            options.LocalToken = Secret;
                    });
            builder.Services.AddAuthorization();

            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0"); // OS-assigned loopback port
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapDirectToolCallApi(dataArguments);
            app.MapSystemToolApi(dataArguments);

            await app.StartAsync();
            var baseUrl = app.Urls.First();
            return new RunningHost(app, baseUrl);
        }

        sealed class RunningHost : IAsyncDisposable
        {
            readonly WebApplication _app;
            public string BaseUrl { get; }

            public RunningHost(WebApplication app, string baseUrl)
            {
                _app = app;
                BaseUrl = baseUrl;
            }

            public async ValueTask DisposeAsync()
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }

        sealed class StubToolHub : IClientToolHub
        {
            public Task<ResponseData<ResponseCallTool>> RunCallTool(RequestCallTool request)
                => Task.FromResult(ResponseData<ResponseCallTool>.Success(request.RequestID));

            public Task<ResponseData<ResponseListTool[]>> RunListTool(RequestListTool request, CancellationToken cancellationToken = default)
                => Task.FromResult(new ResponseData<ResponseListTool[]>(request.RequestID, ResponseStatus.Success)
                {
                    Value = Array.Empty<ResponseListTool>()
                });
        }

        sealed class StubSystemToolHub : IClientSystemToolHub
        {
            public Task<ResponseData<ResponseCallTool>> RunSystemTool(RequestCallTool request, CancellationToken cancellationToken = default)
                => Task.FromResult(ResponseData<ResponseCallTool>.Success(request.RequestID));

            public Task<ResponseData<ResponseListTool[]>> RunListSystemTool(RequestListTool request, CancellationToken cancellationToken = default)
                => Task.FromResult(new ResponseData<ResponseListTool[]>(request.RequestID, ResponseStatus.Success)
                {
                    Value = Array.Empty<ResponseListTool>()
                });
        }
    }
}
