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
using com.IvanMurzak.McpPlugin.Server.Auth.OAuth;
using com.IvanMurzak.McpPlugin.Server.Tests.OAuth;
using com.IvanMurzak.McpPlugin.Server.Webhooks.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// 🔒 Auth-gate PARITY for the pinned REST tool routes (design 06 / zero-config-engine-connect b1):
    /// the pinned group <c>/p/{pin}/api/tools</c> gates IDENTICALLY to the unpinned group
    /// <c>/api/tools</c> and to the MCP transport — the pin is routing only, never identity. Every
    /// assertion probes the pinned AND the unpinned route and requires the SAME outcome:
    /// <list type="bullet">
    ///   <item>401 when unauthenticated in every credential-bearing mode (oauth / token / required);
    ///   open only in none mode.</item>
    ///   <item>A wrong-audience JWT is rejected (the shared <see cref="AccessTokenValidator"/> agent-plane
    ///   audience check applies to the pinned surface too).</item>
    ///   <item>A valid PAT via the opaque-token introspection path is accepted.</item>
    /// </list>
    /// Because the two groups share the same auth pipeline (<c>UseAuthentication</c> + endpoint
    /// <c>RequireAuthorization</c>) and the same <see cref="AuthGating"/> decision, the pinned surface can
    /// never be an unauthenticated bypass — these tests pin that guarantee against regressions.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class PinnedDirectToolAuthGatingTests
    {
        const string Secret = "pinned-rest-gate-secret";
        const string Pin = "aabbccdd";

        static string PinnedTools => $"/p/{Pin}/api/tools";
        const string UnpinnedTools = "/api/tools";

        // OAuth resource-server config for the real-validator tests (mirrors AccessTokenValidatorTests).
        const string Issuer = "https://as.example";
        const string Resource = "http://localhost:23471";
        const string Kid = "key-1";
        static readonly DateTimeOffset Now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        // ─────────────── 401 parity across credential-bearing modes (no token) ───────────────

        [Fact]
        public async Task OAuthMode_WithoutToken_PinnedAndUnpinned_Return401()
        {
            await using var host = await StartTokenSchemeHostAsync(Consts.MCP.Server.AuthOption.oauth);
            using var client = NewClient(host);

            (await GetStatusAsync(client, PinnedTools)).ShouldBe(HttpStatusCode.Unauthorized);
            (await GetStatusAsync(client, UnpinnedTools)).ShouldBe(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task TokenMode_WithoutToken_PinnedAndUnpinned_Return401()
        {
            await using var host = await StartTokenSchemeHostAsync(Consts.MCP.Server.AuthOption.token);
            using var client = NewClient(host);

            (await GetStatusAsync(client, PinnedTools)).ShouldBe(HttpStatusCode.Unauthorized);
            (await GetStatusAsync(client, UnpinnedTools)).ShouldBe(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task RequiredAliasMode_WithoutToken_PinnedAndUnpinned_Return401()
        {
            await using var host = await StartTokenSchemeHostAsync(Consts.MCP.Server.AuthOption.required);
            using var client = NewClient(host);

            (await GetStatusAsync(client, PinnedTools)).ShouldBe(HttpStatusCode.Unauthorized);
            (await GetStatusAsync(client, UnpinnedTools)).ShouldBe(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task TokenMode_WithValidToken_PinnedAndUnpinned_AreReachable()
        {
            await using var host = await StartTokenSchemeHostAsync(Consts.MCP.Server.AuthOption.token);
            using var client = NewClient(host);

            (await GetStatusAsync(client, PinnedTools, bearer: Secret)).ShouldBe(HttpStatusCode.OK);
            (await GetStatusAsync(client, UnpinnedTools, bearer: Secret)).ShouldBe(HttpStatusCode.OK);
        }

        [Fact]
        public async Task NoneMode_WithoutToken_PinnedAndUnpinned_AreOpen()
        {
            await using var host = await StartTokenSchemeHostAsync(Consts.MCP.Server.AuthOption.none);
            using var client = NewClient(host);

            (await GetStatusAsync(client, PinnedTools)).ShouldBe(HttpStatusCode.OK);
            (await GetStatusAsync(client, UnpinnedTools)).ShouldBe(HttpStatusCode.OK);
        }

        // ─────────────── OAuth real-validator parity: wrong-audience JWT + PAT introspection ───────────────

        [Fact]
        public async Task OAuthMode_WrongAudienceJwt_PinnedAndUnpinned_Rejected401()
        {
            using var key = TestJwt.CreateKey();
            var validator = new AccessTokenValidator(
                new OAuthResourceServerConfig(Issuer, Resource),
                new FakeJwksKeyProvider().Add(Kid, key),
                FakeIntrospectionClient.AlwaysInactive,
                () => Now);
            await using var host = await StartOAuthHostAsync(validator, new OAuthResourceServerConfig(Issuer, Resource));
            using var client = NewClient(host);

            // Correctly signed, unexpired ES256 JWT — but audienced to the WRONG resource → rejected.
            var wrongAud = TestJwt.SignEs256(key, Kid, TestJwt.Claims(Issuer, "http://localhost:59999", Now.AddHours(1)));

            (await GetStatusAsync(client, PinnedTools, bearer: wrongAud)).ShouldBe(HttpStatusCode.Unauthorized);
            (await GetStatusAsync(client, UnpinnedTools, bearer: wrongAud)).ShouldBe(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task OAuthMode_ValidJwt_PinnedAndUnpinned_AreReachable()
        {
            // Positive control: a JWT audienced to the canonical resource is accepted on BOTH surfaces.
            using var key = TestJwt.CreateKey();
            var validator = new AccessTokenValidator(
                new OAuthResourceServerConfig(Issuer, Resource),
                new FakeJwksKeyProvider().Add(Kid, key),
                FakeIntrospectionClient.AlwaysInactive,
                () => Now);
            await using var host = await StartOAuthHostAsync(validator, new OAuthResourceServerConfig(Issuer, Resource));
            using var client = NewClient(host);

            var goodJwt = TestJwt.SignEs256(key, Kid, TestJwt.Claims(Issuer, Resource, Now.AddHours(1)));

            (await GetStatusAsync(client, PinnedTools, bearer: goodJwt)).ShouldBe(HttpStatusCode.OK);
            (await GetStatusAsync(client, UnpinnedTools, bearer: goodJwt)).ShouldBe(HttpStatusCode.OK);
        }

        [Fact]
        public async Task OAuthMode_ValidPatViaIntrospection_PinnedAndUnpinned_AreReachable()
        {
            using var key = TestJwt.CreateKey();
            const string Pat = "agd_pat_abc";
            var introspection = new FakeIntrospectionClient(t =>
                t == Pat ? new IntrospectionResult(true, "pat-user", "mcp:agent", Now.AddHours(1)) : IntrospectionResult.Inactive);
            var validator = new AccessTokenValidator(
                new OAuthResourceServerConfig(Issuer, Resource),
                new FakeJwksKeyProvider().Add(Kid, key),
                introspection,
                () => Now);
            await using var host = await StartOAuthHostAsync(validator, new OAuthResourceServerConfig(Issuer, Resource));
            using var client = NewClient(host);

            (await GetStatusAsync(client, PinnedTools, bearer: Pat)).ShouldBe(HttpStatusCode.OK);
            (await GetStatusAsync(client, UnpinnedTools, bearer: Pat)).ShouldBe(HttpStatusCode.OK);
        }

        [Fact]
        public async Task OAuthMode_InactivePat_PinnedAndUnpinned_Rejected401()
        {
            using var key = TestJwt.CreateKey();
            var validator = new AccessTokenValidator(
                new OAuthResourceServerConfig(Issuer, Resource),
                new FakeJwksKeyProvider().Add(Kid, key),
                FakeIntrospectionClient.AlwaysInactive, // every opaque token introspects as inactive
                () => Now);
            await using var host = await StartOAuthHostAsync(validator, new OAuthResourceServerConfig(Issuer, Resource));
            using var client = NewClient(host);

            (await GetStatusAsync(client, PinnedTools, bearer: "agd_pat_unknown")).ShouldBe(HttpStatusCode.Unauthorized);
            (await GetStatusAsync(client, UnpinnedTools, bearer: "agd_pat_unknown")).ShouldBe(HttpStatusCode.Unauthorized);
        }

        // ───────────────────────────────── helpers ─────────────────────────────────

        static HttpClient NewClient(RunningHost host) => new HttpClient { BaseAddress = new Uri(host.BaseUrl) };

        static async Task<HttpStatusCode> GetStatusAsync(HttpClient client, string route, string? bearer = null)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, route);
            if (bearer != null)
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearer}");
            using var resp = await client.SendAsync(req);
            return resp.StatusCode;
        }

        // Token-scheme host — mirrors DirectToolEndpointsAuthGatingTests but ALSO maps the pinned group.
        static async Task<RunningHost> StartTokenSchemeHostAsync(Consts.MCP.Server.AuthOption authOption)
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
            app.Urls.Add("http://127.0.0.1:0");
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapDirectToolCallApi(dataArguments);
            app.MapPinnedDirectToolCallApi(dataArguments);

            await app.StartAsync();
            return new RunningHost(app, app.Urls.First());
        }

        // OAuth host wired with a REAL AccessTokenValidator (hermetic JWKS + introspection fakes), so the
        // wrong-audience / PAT-introspection behavior on the pinned surface is the genuine production path.
        static async Task<RunningHost> StartOAuthHostAsync(IOAuthTokenValidator validator, OAuthResourceServerConfig config)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();

            var dataArguments = Mock.Of<IDataArguments>(d => d.Authorization == Consts.MCP.Server.AuthOption.oauth);
            builder.Services.AddSingleton(dataArguments);
            builder.Services.AddSingleton<IClientToolHub>(new StubToolHub());
            builder.Services.AddSingleton<IClientSystemToolHub>(new StubSystemToolHub());
            // The OAuth auth path calls IAuthorizationWebhookService.AuthorizeAiAgentAsync after a valid token;
            // use the NoOp (returns true) — a bare Moq mock returns a null Task, which would throw on await.
            builder.Services.AddSingleton<IAuthorizationWebhookService>(new NoOpAuthorizationWebhookService());
            builder.Services.AddSingleton(validator);
            builder.Services.AddSingleton(config);

            builder.Services
                .AddAuthentication(TokenAuthenticationHandler.SchemeName)
                .AddScheme<TokenAuthenticationOptions, TokenAuthenticationHandler>(
                    TokenAuthenticationHandler.SchemeName,
                    options => { options.OAuthMode = true; });
            builder.Services.AddAuthorization();

            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapDirectToolCallApi(dataArguments);
            app.MapPinnedDirectToolCallApi(dataArguments);

            await app.StartAsync();
            return new RunningHost(app, app.Urls.First());
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
