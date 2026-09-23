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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    /// <summary>
    /// <see cref="ProjectKeyProvider"/> get-or-mint behaviour (project-keys contract §2/§6) against a fake
    /// server: reuse only when the cached key belongs to the signed-in account AND the server still accepts
    /// it (or cannot be reached); mint otherwise; never mint without a login.
    /// </summary>
    public sealed class ProjectKeyProviderTests : IDisposable
    {
        private const string Pin = "aabbccdd";
        private readonly string _baseDir = Path.Combine(Path.GetTempPath(), "agd-pk-prov-" + Guid.NewGuid().ToString("N"), ".ai-game-dev");

        public void Dispose()
        {
            var parent = Path.GetDirectoryName(_baseDir);
            if (parent != null && Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }

        /// <summary>An unsigned JWT-shaped access token whose payload carries <paramref name="sub"/>.</summary>
        internal static string Jwt(string sub)
        {
            static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return B64("{\"alg\":\"ES256\",\"kid\":\"k\"}") + "." + B64("{\"sub\":\"" + sub + "\",\"scope\":\"mcp:plugin\"}") + ".c2ln";
        }

        private sealed class FakeServer : HttpMessageHandler
        {
            public readonly List<(HttpMethod Method, string Path, string? Bearer, string? Body)> Requests = new();
            public Func<HttpStatusCode> CurrentStatus = () => HttpStatusCode.OK;
            public Exception? CurrentThrows;
            public HttpStatusCode MintStatus = HttpStatusCode.Created;
            public Action? OnMint;
            public string CurrentPin = Pin;
            private int _minted;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                Requests.Add((request.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.Parameter, body));

                if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath == ProjectKeyProvider.CurrentPath)
                {
                    if (CurrentThrows != null)
                        throw CurrentThrows;
                    return new HttpResponseMessage(CurrentStatus())
                    {
                        Content = new StringContent("{\"key_id\":\"pk_x\",\"project_pin\":\"" + CurrentPin + "\",\"active\":true}"),
                    };
                }
                if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == ProjectKeyProvider.MintPath)
                {
                    OnMint?.Invoke();
                    if (MintStatus != HttpStatusCode.Created)
                        return new HttpResponseMessage(MintStatus) { Content = new StringContent("{\"error\":\"nope\"}") };
                    var pin = (string)JsonNode.Parse(body!)!["project_pin"]!;
                    _minted++;
                    return new HttpResponseMessage(HttpStatusCode.Created)
                    {
                        Content = new StringContent("{\"key\":\"agd_pk_minted_" + _minted + "\",\"key_id\":\"pk_" + _minted + "\",\"project_pin\":\"" + pin + "\",\"created_at\":\"2026-09-23T00:00:00Z\"}"),
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }

        private ProjectKeyProvider Provider(FakeServer server, Func<string?> token, string issuer = "https://ai-game.dev")
            => new ProjectKeyProvider(_ => Task.FromResult(token()), issuer, new ProjectKeyStore(_baseDir), new HttpClient(server));

        private ProjectKeyStore Store => new ProjectKeyStore(_baseDir);

        private void SeedCache(string key, string sub) => Store.Put(new ProjectKeyEntry
        {
            Key = key, KeyId = "pk_old", Pin = Pin, Issuer = "https://ai-game.dev", Sub = sub, Engine = "unity",
        });

        [Fact]
        public async Task NoLogin_ReturnsNull_WithoutAnyRequest()
        {
            var server = new FakeServer();
            (await Provider(server, () => null).GetOrMintAsync(Pin, "unity", "PC")).ShouldBeNull();
            server.Requests.ShouldBeEmpty();
        }

        [Fact]
        public async Task CacheMiss_MintsWithTheAccessToken_AndCachesUnderIssuerHashPin()
        {
            var server = new FakeServer();
            var token = Jwt("usr_1");

            var key = await Provider(server, () => token, issuer: "HTTPS://AI-GAME.DEV/").GetOrMintAsync("AABBCCDD", "Unity", "PC-1", "/proj/game");

            key.ShouldBe("agd_pk_minted_1");
            server.Requests.Count.ShouldBe(1);
            var mint = server.Requests[0];
            mint.Method.ShouldBe(HttpMethod.Post);
            mint.Path.ShouldBe("/api/mcp/project-keys");
            mint.Bearer.ShouldBe(token);
            var body = JsonNode.Parse(mint.Body!)!;
            ((string)body["project_pin"]!).ShouldBe(Pin);
            ((string)body["engine"]!).ShouldBe("unity");
            ((string)body["machine_name"]!).ShouldBe("PC-1");
            ((string)body["label"]!).ShouldBe("/proj/game");

            var cached = Store.Get("https://ai-game.dev", Pin)!;
            cached.Key.ShouldBe("agd_pk_minted_1");
            cached.Sub.ShouldBe("usr_1");
            cached.KeyId.ShouldBe("pk_1");
            cached.CreatedAt.ShouldBe("2026-09-23T00:00:00Z");
        }

        [Fact]
        public async Task CachedKey_SameAccount_ServerAccepts_IsReused_NoMint()
        {
            SeedCache("agd_pk_cached", "usr_1");
            var server = new FakeServer();

            (await Provider(server, () => Jwt("usr_1")).GetOrMintAsync(Pin, "unity", "PC")).ShouldBe("agd_pk_cached");

            server.Requests.Count.ShouldBe(1);
            server.Requests[0].Method.ShouldBe(HttpMethod.Get);
            server.Requests[0].Bearer.ShouldBe("agd_pk_cached"); // validated WITH the key, not the login token
        }

        [Fact]
        public async Task CachedKey_Revoked401_IsReplacedByAFreshMint()
        {
            SeedCache("agd_pk_cached", "usr_1");
            var server = new FakeServer { CurrentStatus = () => HttpStatusCode.Unauthorized };

            (await Provider(server, () => Jwt("usr_1")).GetOrMintAsync(Pin, "unity", "PC")).ShouldBe("agd_pk_minted_1");
            Store.Get("https://ai-game.dev", Pin)!.Key.ShouldBe("agd_pk_minted_1");
        }

        [Fact]
        public async Task CachedKey_ServerReportsADifferentPin_IsNotReused()
        {
            SeedCache("agd_pk_cached", "usr_1");
            var server = new FakeServer { CurrentPin = "11223344" };

            (await Provider(server, () => Jwt("usr_1")).GetOrMintAsync(Pin, "unity", "PC")).ShouldBe("agd_pk_minted_1");
        }

        [Fact]
        public async Task CachedKey_TransientServerError_IsReused()
        {
            SeedCache("agd_pk_cached", "usr_1");
            var server = new FakeServer { CurrentStatus = () => HttpStatusCode.BadGateway };

            (await Provider(server, () => Jwt("usr_1")).GetOrMintAsync(Pin, "unity", "PC")).ShouldBe("agd_pk_cached");
            server.Requests.ShouldAllBe(r => r.Method == HttpMethod.Get);
        }

        [Theory]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.NotFound)]
        public async Task CachedKey_Non401Failure_IsReused(HttpStatusCode status)
        {
            // Contract §6: only a 401 means revoked — any other failure is transient and must not mint a new key.
            SeedCache("agd_pk_cached", "usr_1");
            var server = new FakeServer { CurrentStatus = () => status };

            (await Provider(server, () => Jwt("usr_1")).GetOrMintAsync(Pin, "unity", "PC")).ShouldBe("agd_pk_cached");
            server.Requests.ShouldAllBe(r => r.Method == HttpMethod.Get);
        }

        [Fact]
        public async Task UnreadableCache_ReturnsNull_WithoutMinting_AndLeavesTheFileUntouched()
        {
            Directory.CreateDirectory(_baseDir);
            var corrupt = new byte[] { 0x7b, 0x00, 0xff, 0x13 };
            File.WriteAllBytes(Store.FilePath, corrupt);
            var server = new FakeServer();

            (await Provider(server, () => Jwt("usr_1")).GetOrMintAsync(Pin, "unity", "PC")).ShouldBeNull();
            (await Provider(server, () => Jwt("usr_1")).RegenerateAsync(Pin, "unity", "PC")).ShouldBeNull();
            server.Requests.ShouldBeEmpty();
            File.ReadAllBytes(Store.FilePath).ShouldBe(corrupt);
        }

        [Fact]
        public async Task ConcurrentGetOrMint_OnOneProvider_MintsOnce()
        {
            var server = new FakeServer();
            var provider = Provider(server, () => Jwt("usr_1"));

            var keys = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => provider.GetOrMintAsync(Pin, "unity", "PC")));

            keys.ShouldAllBe(k => k == "agd_pk_minted_1");
            server.Requests.Count(r => r.Method == HttpMethod.Post).ShouldBe(1);
        }

        [Fact]
        public void FromMachineCredentials_Issuer_IsTheCredentialsServerTargetOrigin()
        {
            ProjectKeyProvider.IssuerFromServerTarget("http://agd.localhost/some/path").ShouldBe("http://agd.localhost");
            ProjectKeyProvider.IssuerFromServerTarget(null).ShouldBe(ProjectKeyProvider.DefaultIssuer);
            ProjectKeyProvider.IssuerFromServerTarget("not a url").ShouldBe(ProjectKeyProvider.DefaultIssuer);

            var creds = new MachineCredentialStore(_baseDir);
            creds.Write(new MachineCredentials { ServerTarget = "http://agd.localhost:8080/" });
            ProjectKeyProvider.FromMachineCredentials(credentialStore: creds).Issuer.ShouldBe("http://agd.localhost:8080");
        }

        [Fact]
        public async Task CachedKey_NetworkFailure_IsReused()
        {
            SeedCache("agd_pk_cached", "usr_1");
            var server = new FakeServer { CurrentThrows = new HttpRequestException("offline") };

            (await Provider(server, () => Jwt("usr_1")).GetOrMintAsync(Pin, "unity", "PC")).ShouldBe("agd_pk_cached");
        }

        [Fact]
        public async Task AccountSwitch_CachedKeyOfAnotherAccount_IsNeverReused()
        {
            SeedCache("agd_pk_other_account", "usr_OTHER");
            var server = new FakeServer();

            (await Provider(server, () => Jwt("usr_1")).GetOrMintAsync(Pin, "unity", "PC")).ShouldBe("agd_pk_minted_1");
            server.Requests.ShouldAllBe(r => r.Method == HttpMethod.Post); // not even validated
            Store.Get("https://ai-game.dev", Pin)!.Sub.ShouldBe("usr_1");
        }

        [Fact]
        public async Task MintRefused_ReturnsNull_AndLeavesTheCacheUntouched()
        {
            SeedCache("agd_pk_other_account", "usr_OTHER");
            var server = new FakeServer { MintStatus = HttpStatusCode.Unauthorized };

            (await Provider(server, () => Jwt("usr_1")).GetOrMintAsync(Pin, "unity", "PC")).ShouldBeNull();
            Store.Get("https://ai-game.dev", Pin)!.Key.ShouldBe("agd_pk_other_account");
        }

        [Fact]
        public async Task Regenerate_AlwaysMints_AndOverwritesAValidCachedKey()
        {
            SeedCache("agd_pk_cached", "usr_1");
            var server = new FakeServer();

            (await Provider(server, () => Jwt("usr_1")).RegenerateAsync(Pin, "godot", "PC")).ShouldBe("agd_pk_minted_1");
            server.Requests.ShouldAllBe(r => r.Method == HttpMethod.Post);
            Store.Get("https://ai-game.dev", Pin)!.Key.ShouldBe("agd_pk_minted_1");
        }

        [Fact]
        public async Task FromMachineCredentials_UsesAnUnexpiredFamilyToken_AndItsSubject()
        {
            var creds = new MachineCredentialStore(_baseDir);
            creds.Write(new MachineCredentials
            {
                Subject = "usr_store",
                Families = new MachineCredentialFamilies
                {
                    Plugin = new MachineCredentialFamily { AccessToken = "expired-plugin", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5) },
                    Agent = new MachineCredentialFamily { AccessToken = "opaque-agent-token", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) },
                },
            });
            var server = new FakeServer();
            var provider = ProjectKeyProvider.FromMachineCredentials(credentialStore: creds, httpClient: new HttpClient(server));

            (await provider.GetOrMintAsync(Pin, "unreal", "PC")).ShouldBe("agd_pk_minted_1");
            server.Requests[0].Bearer.ShouldBe("opaque-agent-token");
            // Opaque token ⇒ the store's recorded subject identifies the account.
            Store.Get("https://ai-game.dev", Pin)!.Sub.ShouldBe("usr_store");
        }

        [Fact]
        public async Task FromMachineCredentials_NoCredentialFile_ReturnsNull()
        {
            var server = new FakeServer();
            var provider = ProjectKeyProvider.FromMachineCredentials(credentialStore: new MachineCredentialStore(_baseDir), httpClient: new HttpClient(server));

            (await provider.GetOrMintAsync(Pin, "unity", "PC")).ShouldBeNull();
            server.Requests.ShouldBeEmpty();
        }

        [Theory]
        [InlineData("unity", "unity")]
        [InlineData("GODOT", "godot")]
        [InlineData(" unreal ", "unreal")]
        [InlineData("cryengine", "unknown")]
        [InlineData(null, "unknown")]
        public void Engine_IsNormalisedToTheServersEnum(string? input, string expected)
            => ProjectKeyProvider.NormalizeEngine(input).ShouldBe(expected);

        [Fact]
        public async Task ConcurrentWriterMintedMeanwhile_TheirEntryIsKept_AndReturned()
        {
            // Another process (another editor, the app) mints and caches a key for the same pin WHILE our mint
            // is in flight (the mint runs outside the lock). Contract §6: keep theirs — every config then agrees.
            var server = new FakeServer();
            server.OnMint = () => SeedCache("agd_pk_theirs", "usr_1");

            (await Provider(server, () => Jwt("usr_1")).GetOrMintAsync(Pin, "unity", "PC")).ShouldBe("agd_pk_theirs");
            Store.Get("https://ai-game.dev", Pin)!.Key.ShouldBe("agd_pk_theirs");
        }

        [Fact]
        public async Task ConcurrentWriterOfAnotherAccount_IsOverwritten_Control()
        {
            // Control for the test above: "keep theirs" applies only to the SAME account's entry.
            var server = new FakeServer();
            server.OnMint = () => SeedCache("agd_pk_other_account", "usr_OTHER");

            (await Provider(server, () => Jwt("usr_1")).GetOrMintAsync(Pin, "unity", "PC")).ShouldBe("agd_pk_minted_1");
            Store.Get("https://ai-game.dev", Pin)!.Key.ShouldBe("agd_pk_minted_1");
        }

        [Fact]
        public async Task Regenerate_OverwritesEvenAConcurrentEntry()
        {
            var server = new FakeServer();
            server.OnMint = () => SeedCache("agd_pk_theirs", "usr_1");

            (await Provider(server, () => Jwt("usr_1")).RegenerateAsync(Pin, "unity", "PC")).ShouldBe("agd_pk_minted_1");
            Store.Get("https://ai-game.dev", Pin)!.Key.ShouldBe("agd_pk_minted_1");
        }

        [Fact]
        public async Task NoStoredSubject_OpaqueTokenInUse_SubFromThePluginFamilyJwt_ReusesTheCachedKey()
        {
            // No credential subject, and the token actually USED is opaque (the plugin JWT has expired), so the
            // token's own claim cannot name the account: only the §6 chain (…else the plugin-family token's sub)
            // does. Without it every call would mint a fresh key.
            var creds = new MachineCredentialStore(_baseDir);
            creds.Write(new MachineCredentials
            {
                Families = new MachineCredentialFamilies
                {
                    Plugin = new MachineCredentialFamily { AccessToken = Jwt("usr_plugin_only"), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5) },
                    Agent = new MachineCredentialFamily { AccessToken = "opaque-agent-token", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) },
                },
            });
            SeedCache("agd_pk_cached", "usr_plugin_only");
            var server = new FakeServer();
            var provider = ProjectKeyProvider.FromMachineCredentials(credentialStore: creds, httpClient: new HttpClient(server));

            (await provider.GetOrMintAsync(Pin, "unity", "PC")).ShouldBe("agd_pk_cached");
            server.Requests.ShouldAllBe(r => r.Method == HttpMethod.Get);
        }

        [Fact]
        public async Task LongMachineNameAndLabel_AreClippedTo120_LabelKeepsItsTail()
        {
            var server = new FakeServer();
            var label = "/Users/someone/" + new string('x', 200) + "/MyGame";

            await Provider(server, () => Jwt("usr_1")).GetOrMintAsync(Pin, "unity", new string('m', 300), label);

            var body = JsonNode.Parse(server.Requests[0].Body!)!;
            ((string)body["machine_name"]!).Length.ShouldBe(ProjectKeyProvider.MaxDisplayFieldLength);
            var sentLabel = (string)body["label"]!;
            sentLabel.Length.ShouldBe(ProjectKeyProvider.MaxDisplayFieldLength);
            sentLabel.ShouldEndWith("/MyGame");
        }

        [Fact]
        public void JwtSubject_IsReadFromThePayload_OpaqueTokensHaveNone()
        {
            ProjectKeyProvider.TryGetJwtSubject(Jwt("usr_42")).ShouldBe("usr_42");
            ProjectKeyProvider.TryGetJwtSubject("agd_pat_opaque").ShouldBeNull();
            ProjectKeyProvider.TryGetJwtSubject("a.!!!.c").ShouldBeNull();
        }
    }
}
