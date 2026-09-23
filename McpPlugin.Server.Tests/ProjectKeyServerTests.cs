/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
└────────────────────────────────────────────────────────────────────────┘
*/

#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Auth.OAuth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using com.IvanMurzak.McpPlugin.Server.Tests.OAuth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// Project keys on the MCP server (project-keys contract §3/§4/§5): an opaque <c>agd_pk_…</c> key whose
    /// introspection carries <c>agd_project_pin</c> is (a) bound to that pin — a request naming another pin is
    /// refused with <c>403 project_pin_mismatch</c>, a request with NO pin is routed as if it named the key's pin
    /// — and (b) rejected outright on the plugin plane (the SignalR hub registration path). JWTs and PATs, which
    /// carry no pin, are unchanged; every rejection test has a control proving the SAME fixture passes when
    /// only the property under test differs.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class ProjectKeyServerTests
    {
        const string Issuer = "https://as.example";
        const string Resource = "http://localhost:23471";
        const string Kid = "key-1";
        const string Account = "usr_pk_owner";
        const string ProjectKey = "agd_pk_0123456789abcdefghijklmnopqrstuvwxyzABCDEF";
        const string Pat = "agd_pat_account_wide_token";
        const string PinA = "aabbccdd";
        const string PinB = "11223344";
        const string HashA = "aabbccdd11223344556677889900aabbccddeeff00112233445566778899aabb";
        const string HashB = "11223344aabbccdd556677889900aabbccddeeff00112233445566778899aabb";
        static readonly DateTimeOffset Now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        static IntrospectionResult KeyIntrospection(string pin = PinA)
            => new IntrospectionResult(true, Account, ConnectionIdentity.ScopeAgent, projectPin: pin, tokenType: AccessTokenValidator.ProjectKeyTokenType);

        static IntrospectionResult PatIntrospection()
            => new IntrospectionResult(true, Account, ConnectionIdentity.ScopeAgent);

        static AccessTokenValidator Validator(Func<string, IntrospectionResult> map, Action? onIntrospect = null)
        {
            using var key = TestJwt.CreateKey();
            var jwks = new FakeJwksKeyProvider().Add(Kid, key);
            return new AccessTokenValidator(new OAuthResourceServerConfig(Issuer, Resource), jwks,
                new FakeIntrospectionClient(t => { onIntrospect?.Invoke(); return map(t); }), () => Now);
        }

        // ── Validator: agent plane carries the pin ────────────────────────────────────────────────

        [Fact]
        public async Task AgentPlane_ProjectKey_Succeeds_CarryingItsBoundPin()
        {
            var result = await Validator(_ => KeyIntrospection()).ValidateAsync(ProjectKey, TokenValidationPlane.Agent, CancellationToken.None);

            result.Succeeded.ShouldBeTrue(result.FailureReason);
            result.Subject.ShouldBe(Account);
            result.ProjectPin.ShouldBe(PinA);
        }

        [Fact]
        public async Task AgentPlane_Pat_StaysAccountWide_NoPin()
        {
            var result = await Validator(_ => PatIntrospection()).ValidateAsync(Pat, TokenValidationPlane.Agent, CancellationToken.None);

            result.Succeeded.ShouldBeTrue(result.FailureReason);
            result.ProjectPin.ShouldBeNull();
        }

        [Fact]
        public async Task AgentPlane_ProjectKeyTokenType_WithoutPin_FailsClosed()
        {
            // An AS response that marks the token a project key but omits the pin must never be read as an
            // account-wide credential.
            var result = await Validator(_ => new IntrospectionResult(true, Account, ConnectionIdentity.ScopeAgent, tokenType: AccessTokenValidator.ProjectKeyTokenType))
                .ValidateAsync("opaque-no-prefix", TokenValidationPlane.Agent, CancellationToken.None);

            result.Succeeded.ShouldBeFalse();
            result.FailureReason!.ShouldContain("project pin");
        }

        // ── Validator: plugin plane rejects project keys (contract §4) ────────────────────────────

        [Fact]
        public async Task PluginPlane_ProjectKey_IsRejected_BeforeIntrospection()
        {
            var introspections = 0;
            var result = await Validator(_ => KeyIntrospection(), () => introspections++)
                .ValidateAsync(ProjectKey, TokenValidationPlane.Plugin, CancellationToken.None);

            result.Succeeded.ShouldBeFalse();
            result.FailureReason!.ShouldContain("plugin plane");
            ConnectionIdentity.Create(result.Subject, result.Scope, result.ClientId).ShouldBeNull("the hub must get no identity ⇒ never registers");
            introspections.ShouldBe(0);
        }

        [Fact]
        public async Task PluginPlane_KeyRecognisedByPinAlone_IsRejected()
        {
            // No agd_pk_ prefix on the value: the introspected pin alone must still trigger the plane rule.
            var result = await Validator(_ => KeyIntrospection()).ValidateAsync("opaque-no-prefix", TokenValidationPlane.Plugin, CancellationToken.None);

            result.Succeeded.ShouldBeFalse();
            result.FailureReason!.ShouldContain("plugin plane");
        }

        [Fact]
        public async Task PluginPlane_Pat_Control_StillValidates()
        {
            // Control for the two rejections above: the SAME opaque path on the plugin plane still accepts a
            // pin-less PAT, so the rejection is specific to project keys and not a broken opaque path.
            var result = await Validator(_ => PatIntrospection()).ValidateAsync(Pat, TokenValidationPlane.Plugin, CancellationToken.None);

            result.Succeeded.ShouldBeTrue(result.FailureReason);
        }

        // ── Introspection parsing (contract §3) ───────────────────────────────────────────────────

        [Fact]
        public async Task Introspection_ParsesPinAndTokenType_LowerCasingThePin()
        {
            var client = new IntrospectionClient((_, _) => Task.FromResult<string?>(
                "{\"active\":true,\"sub\":\"u1\",\"scope\":\"mcp:agent\",\"client_id\":\"agd_project_key\",\"token_type\":\"project_key\",\"agd_project_pin\":\"AABBCCDD\"}"));

            var result = await client.IntrospectAsync(ProjectKey, CancellationToken.None);

            result.Active.ShouldBeTrue();
            result.ProjectPin.ShouldBe(PinA);
            result.TokenType.ShouldBe("project_key");
            result.ExpiresAt.ShouldBeNull("a project key never expires (no exp member)");
        }

        [Theory]
        [InlineData("\"aabbcc\"")]      // too short
        [InlineData("\"aabbccdd00\"")]  // too long
        [InlineData("\"aabbccxz\"")]    // not hex
        [InlineData("12345678")]        // not a string
        [InlineData("null")]
        public async Task Introspection_MalformedPin_FailsClosed_Inactive(string pinJson)
        {
            var client = new IntrospectionClient((_, _) => Task.FromResult<string?>(
                "{\"active\":true,\"sub\":\"u1\",\"scope\":\"mcp:agent\",\"agd_project_pin\":" + pinJson + "}"));

            (await client.IntrospectAsync(ProjectKey, CancellationToken.None)).Active.ShouldBeFalse();
        }

        // ── Pin binding rule (contract §5), pure ──────────────────────────────────────────────────

        static ClaimsPrincipal KeyPrincipal(string pin) => new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(TokenAuthenticationHandler.SubjectClaimType, Account), new Claim(TokenAuthenticationHandler.ProjectPinClaimType, pin) }, "test"));

        [Theory]
        [InlineData(ProjectPinParse.Absent, null, McpSessionTokenMiddleware.ProjectKeyPinCheck.Bound, PinA)]
        [InlineData(ProjectPinParse.Valid, PinA, McpSessionTokenMiddleware.ProjectKeyPinCheck.Bound, PinA)]
        [InlineData(ProjectPinParse.Valid, "AABBCCDD", McpSessionTokenMiddleware.ProjectKeyPinCheck.Bound, "AABBCCDD")]
        [InlineData(ProjectPinParse.Valid, PinB, McpSessionTokenMiddleware.ProjectKeyPinCheck.Mismatch, null)]
        public void ResolveEffectiveProjectPin_BindsToTheKeyPin(ProjectPinParse parse, string? pathPin, McpSessionTokenMiddleware.ProjectKeyPinCheck expected, string? expectedPin)
        {
            McpSessionTokenMiddleware.ResolveEffectiveProjectPin(parse, pathPin, KeyPrincipal(PinA), out var effective).ShouldBe(expected);
            effective.ShouldBe(expectedPin);
        }

        [Fact]
        public void ResolveEffectiveProjectPin_AccountWidePrincipal_LeavesThePathPinAlone()
        {
            var jwt = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(TokenAuthenticationHandler.SubjectClaimType, Account) }, "test"));

            McpSessionTokenMiddleware.ResolveEffectiveProjectPin(ProjectPinParse.Valid, PinB, jwt, out var effective)
                .ShouldBe(McpSessionTokenMiddleware.ProjectKeyPinCheck.NotProjectKey);
            effective.ShouldBe(PinB);
            McpSessionTokenMiddleware.ResolveEffectiveProjectPin(ProjectPinParse.Absent, null, jwt, out effective)
                .ShouldBe(McpSessionTokenMiddleware.ProjectKeyPinCheck.NotProjectKey);
            effective.ShouldBeNull();
        }

        // ── Over HTTP, REAL production wiring (auth=oauth): 403 on a foreign pin ──────────────────

        static Task<OAuthMcpHost> StartOAuthHostAsync()
            => OAuthMcpHost.StartAsync(services => services.AddSingleton<IIntrospectionClient>(
                new FakeIntrospectionClient(t => t == ProjectKey ? KeyIntrospection(PinA) : IntrospectionResult.Inactive)),
                account: Account);

        static async Task<(HttpStatusCode Status, string Body)> GetAsync(OAuthMcpHost host, string path, string bearer)
        {
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, host.BaseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            using var response = await client.SendAsync(request);
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task OverHttp_ProjectKey_OnAnotherProjectsPin_Is403_ProjectPinMismatch()
        {
            await using var host = await StartOAuthHostAsync();

            var (status, body) = await GetAsync(host, $"/p/{PinB}/api/tools", ProjectKey);

            status.ShouldBe(HttpStatusCode.Forbidden);
            body.ShouldContain(McpSessionTokenMiddleware.ProjectPinMismatchError);
        }

        [Fact]
        public async Task OverHttp_ProjectKey_McpEndpoint_OnAnotherProjectsPin_Is403()
        {
            await using var host = await StartOAuthHostAsync();

            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, host.BaseUrl + $"/mcp/p/{PinB}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ProjectKey);
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("text/event-stream");
            request.Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{},\"clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}}",
                System.Text.Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request);

            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await response.Content.ReadAsStringAsync()).ShouldContain(McpSessionTokenMiddleware.ProjectPinMismatchError);
        }

        [Theory]
        [InlineData("/p/" + PinA + "/api/tools")]   // its own pin
        [InlineData("/p/AABBCCDD/api/tools")]         // its own pin, upper-case (case-insensitive)
        [InlineData("/api/tools")]                    // no pin — routed as its own pin
        public async Task OverHttp_ProjectKey_OnItsOwnOrNoPin_IsAuthorized_Control(string path)
        {
            await using var host = await StartOAuthHostAsync();

            var (status, body) = await GetAsync(host, path, ProjectKey);

            // Control for the 403 above: the SAME key on the SAME host is authenticated and admitted.
            status.ShouldNotBe(HttpStatusCode.Forbidden, body);
            status.ShouldNotBe(HttpStatusCode.Unauthorized, body);
        }

        [Fact]
        public async Task OverHttp_AccountWideJwt_OnAnyPin_IsNotRefused_Unchanged()
        {
            await using var host = await StartOAuthHostAsync();

            var (status, body) = await GetAsync(host, $"/p/{PinB}/api/tools", host.TokenForAccount(Account));

            status.ShouldNotBe(HttpStatusCode.Forbidden, body);
            status.ShouldNotBe(HttpStatusCode.Unauthorized, body);
        }

        // ── Over HTTP: an unpinned project-key request ROUTES to the key's project, never MRU ─────

        [Fact]
        public async Task OverHttp_ProjectKey_WithoutPathPin_ResolvesToTheKeysProject_NeverTheSibling()
        {
            var instances = new AccountInstances();
            instances.Register(Account, new PluginInstanceMetadata("instance-A", "unity", "GameA", HashA, "PC-1"), "conn-A");
            instances.Register(Account, new PluginInstanceMetadata("instance-B", "godot", "GameB", HashB, "PC-2"), "conn-B");

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            await using var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            // Stand-in for UseAuthentication: a principal carrying the key's pin claim, exactly as the
            // TokenAuthenticationHandler OAuth path issues it for a project key.
            app.Use(async (ctx, next) =>
            {
                var pin = ctx.Request.Headers["X-Test-Key-Pin"].ToString();
                if (!string.IsNullOrEmpty(pin))
                    ctx.User = KeyPrincipal(pin);
                await next();
            });
            app.UseMiddleware<McpSessionTokenMiddleware>();
            RequestDelegate resolve = ctx =>
            {
                var r = instances.Resolve(Account, McpSessionTokenContext.CurrentProjectPin, selectedInstanceId: null);
                return ctx.Response.WriteAsync(r.Kind == InstanceResolutionKind.Resolved ? r.Instance!.InstanceId : r.Kind.ToString());
            };
            app.MapGet("/api/tools", resolve);
            app.MapGet("/p/{pin}/api/tools", resolve);
            await app.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
            async Task<string> Get(string path, string? keyPin)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, path);
                if (keyPin != null)
                    req.Headers.Add("X-Test-Key-Pin", keyPin);
                using var resp = await client.SendAsync(req);
                return await resp.Content.ReadAsStringAsync();
            }

            // Both projects are reachable by their own keys with NO pin in the path — each lands on its own
            // instance (with two live instances an unpinned account-wide request would be MRU-ambiguous).
            (await Get("/api/tools", PinA)).ShouldBe("instance-A");
            (await Get("/api/tools", PinB)).ShouldBe("instance-B");
            (await Get($"/p/{PinA}/api/tools", PinA)).ShouldBe("instance-A");
            await app.StopAsync();
        }
    }
}
