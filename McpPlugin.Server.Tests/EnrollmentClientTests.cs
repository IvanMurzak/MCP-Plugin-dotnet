/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Server.Tools;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// Unit tests for the RS-side enrollment proxy (mcp-authorize b4, design 05 enroll/create) against
    /// a MOCKED authorization server via the injected <see cref="EnrollCreatePost"/> delegate — no live
    /// HttpClient. Real staging-AS e2e is deferred to the integration gates (b8).
    /// </summary>
    public class EnrollmentClientTests
    {
        const string PublicUrl = "https://ai-game.dev/mcp";

        static EnrollmentClient WithResponse(string? json, System.Action<string, string, string>? capture = null)
        {
            EnrollCreatePost post = (bearer, engine, publicUrl, ct) =>
            {
                capture?.Invoke(bearer, engine, publicUrl);
                return Task.FromResult(json);
            };
            return new EnrollmentClient(post, PublicUrl);
        }

        [Fact]
        public async Task CreateAsync_ForwardsBearerEngineAndPublicUrl_ToTheAs()
        {
            string? seenBearer = null, seenEngine = null, seenPublicUrl = null;
            var client = WithResponse("{\"enroll_code\":\"ABC123\"}",
                (b, e, u) => { seenBearer = b; seenEngine = e; seenPublicUrl = u; });

            await client.CreateAsync("godot", "jwt-or-pat-token", CancellationToken.None);

            seenBearer.ShouldBe("jwt-or-pat-token");
            seenEngine.ShouldBe("godot");
            seenPublicUrl.ShouldBe(PublicUrl);
        }

        [Fact]
        public async Task CreateAsync_ParsesEnrollCode_OnSuccess()
        {
            var result = await WithResponse("{\"enroll_code\":\"XYZ789\",\"expires_in\":300}")
                .CreateAsync("unity", "token", CancellationToken.None);

            result.Success.ShouldBeTrue();
            result.EnrollCode.ShouldBe("XYZ789");
            result.Error.ShouldBeNull();
        }

        [Fact]
        public async Task CreateAsync_ParsesOptionalInstallHint()
        {
            var result = await WithResponse("{\"enroll_code\":\"XYZ789\",\"expires_in\":300,\"install_hint\":\"Paste {code} into the installer.\"}")
                .CreateAsync("brutal", "token", CancellationToken.None);

            result.Success.ShouldBeTrue();
            result.EnrollCode.ShouldBe("XYZ789");
            result.InstallHint.ShouldBe("Paste {code} into the installer.");
        }

        [Theory]
        [InlineData("{\"enroll_code\":\"XYZ789\",\"expires_in\":300}")]                       // older AS: no hint
        [InlineData("{\"enroll_code\":\"XYZ789\",\"expires_in\":300,\"install_hint\":null}")] // unknown engine
        [InlineData("{\"enroll_code\":\"XYZ789\",\"install_hint\":\"  \"}")]
        [InlineData("{\"enroll_code\":\"XYZ789\",\"install_hint\":42}")]
        public async Task CreateAsync_ToleratesMissingOrInvalidInstallHint(string json)
        {
            var result = await WithResponse(json).CreateAsync("unity", "token", CancellationToken.None);

            result.Success.ShouldBeTrue();
            result.EnrollCode.ShouldBe("XYZ789");
            result.InstallHint.ShouldBeNull();
        }

        [Fact]
        public async Task CreateAsync_ForwardsAnArbitraryEngineId_Verbatim()
        {
            string? seenEngine = null;
            var client = WithResponse("{\"enroll_code\":\"ABC\"}", (b, e, u) => seenEngine = e);

            await client.CreateAsync("my-engine", "token", CancellationToken.None);

            seenEngine.ShouldBe("my-engine");
        }

        [Fact]
        public async Task CreateAsync_NullResponse_IsFailure()
        {
            var result = await WithResponse(null).CreateAsync("unity", "token", CancellationToken.None);
            result.Success.ShouldBeFalse();
            result.EnrollCode.ShouldBeNull();
            result.Error.ShouldNotBeNull();
        }

        [Fact]
        public async Task CreateAsync_MalformedJson_IsFailure()
        {
            var result = await WithResponse("not json").CreateAsync("unity", "token", CancellationToken.None);
            result.Success.ShouldBeFalse();
            result.Error.ShouldNotBeNull();
        }

        [Fact]
        public async Task CreateAsync_MissingEnrollCode_IsFailure()
        {
            var result = await WithResponse("{\"something_else\":\"1\"}").CreateAsync("unity", "token", CancellationToken.None);
            result.Success.ShouldBeFalse();
            result.Error.ShouldNotBeNull();
        }

        [Fact]
        public async Task CreateAsync_NoCredential_IsFailure_WithoutCallingTheAs()
        {
            var called = false;
            EnrollCreatePost post = (b, e, u, ct) => { called = true; return Task.FromResult<string?>("{\"enroll_code\":\"x\"}"); };
            var client = new EnrollmentClient(post, PublicUrl);

            var result = await client.CreateAsync("unity", "", CancellationToken.None);

            result.Success.ShouldBeFalse();
            called.ShouldBeFalse();
        }
    }
}
