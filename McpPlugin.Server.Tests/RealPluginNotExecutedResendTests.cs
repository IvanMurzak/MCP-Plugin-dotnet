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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using com.IvanMurzak.ReflectorNet;
using Shouldly;
using Xunit;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// The server re-sends a call the plugin refused as not executed (same request id). Wave-6 finding: the REAL
    /// McpPlugin client cached that refusal per request id and answered the re-send with it, so the tool never ran.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class RealPluginNotExecutedResendTests
    {
        [Fact]
        public async Task ResendAfterNotExecuted_RunsTheToolAgain()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            var projectRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mcpplugin-notexecuted-" + Guid.NewGuid().ToString("N"))).FullName;

            var runs = 0;
            var tool = new ProxyTool("flaky", null, "Refuses its first call as not executed.", null, null, null, null, null, null, null, null,
                (requestId, arguments, cancellationToken) => Task.FromResult(Interlocked.Increment(ref runs) == 1
                    ? ResponseCallTool.NotExecuted("Reloading; the call was not executed.")
                    : ResponseCallTool.Success("ran")));

            using var plugin = new McpPluginBuilder(new Version { Api = Consts.ApiVersion, Plugin = "0.0.0-test", Environment = "test-engine" })
                .WithConfig(config =>
                {
                    config.Host = host.BaseUrl;
                    config.ProjectRootPath = projectRoot;
                    config.GenerateSkillFiles = false;
                })
                .AddTool("flaky", tool)
                .Build(new Reflector());
            try
            {
                await plugin.Connect(CancellationToken.None);
                var until = DateTime.UtcNow + TimeSpan.FromSeconds(15);
                while (plugin.VersionHandshakeStatus?.Compatible != true && DateTime.UtcNow < until)
                    await Task.Delay(50);
                plugin.VersionHandshakeStatus?.Compatible.ShouldBe(true);

                var (isError, text) = await client.CallAsync("flaky", new { }).WaitAsync(TimeSpan.FromSeconds(30));
                isError.ShouldBeFalse(text);
                text.ShouldBe("ran");
                runs.ShouldBe(2, "the re-send must reach the tool, once");
            }
            finally
            {
                await plugin.Disconnect();
                try { Directory.Delete(projectRoot, recursive: true); } catch (IOException) { }
            }
        }
    }
}
