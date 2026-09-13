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
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// The first test in this repository that drives a REAL plugin against the REAL server host over
    /// the REAL transport: <see cref="NoneAuthMcpHost"/> (Kestrel, auth=none, Streamable HTTP) with
    /// the <c>McpPlugin.NullEngine</c> executable spawned as an actual OS process and connected over
    /// SignalR. Every other server suite either builds strategy / identity objects directly or
    /// registers a fake <c>IClientToolHub</c>, so before this class nothing exercised the client
    /// library and the server together.
    ///
    /// <para><b>Why a subprocess, and why a derived path rather than a ProjectReference.</b> The
    /// separate OS process is the POINT, not an isolation trick: a real headless host process
    /// reached over a real transport is exactly what this class exists to cover, and an in-process
    /// reference could not be one. It is NOT about keeping the client assembly out of this process
    /// — this csproj already references <c>McpPlugin</c> (for <c>ProjectIdentity</c>), so the
    /// client library is loaded here anyway. The engine is LOCATED by a path derived from this
    /// assembly's own bin directory (env override <c>MCPPLUGIN_NULLENGINE_DLL</c>), relying on
    /// solution build order. <c>ProjectReference ReferenceOutputAssembly="false"</c> is the
    /// alternative if derivation ever proves brittle; it would add a build-order coupling and copy
    /// the engine's outputs beside this test's, which derivation does not.</para>
    ///
    /// <para><b>Why one plugin at a time.</b> <c>NoAuthMcpStrategy.OnPluginConnected</c> enforces a
    /// single plugin connection, and the client registry it uses (<c>ClientUtils</c>) is keyed by hub
    /// TYPE and therefore process-global. A second engine connecting anywhere in this process would
    /// evict the first. Both tests below start and stop their own engine, and the class joins the
    /// <c>McpPlugin.Server</c> collection, which serialises it against every OTHER class in that
    /// SAME collection. That is the available protection, not a whole-assembly guarantee: xUnit
    /// still runs classes carrying NO <c>[Collection]</c> in parallel with this one, so the
    /// registry could in principle be disturbed from there.</para>
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class NullEngineRealTransportTests
    {
        /// <summary>
        /// The exact catalog the engine must publish. Duplicated from the engine's own source rather
        /// than referenced, because this project deliberately does not reference that assembly (see
        /// the class remarks). A rename on either side must break this list - that is the point.
        ///
        /// <para>That reasoning governs EVERY engine-derived literal in this region, not just this
        /// array: replacing any of them with a reference to the engine's own constant would make a
        /// coordinated rename on both sides keep the test green, which is exactly the failure these
        /// assertions exist to catch.</para>
        /// </summary>
        static readonly string[] ExpectedToolNames =
        {
            "null-engine/echo",
            "null-engine/enum-arg",
            "null-engine/fail",
            "null-engine/large-payload",
            "null-engine/list-self",
            "null-engine/nested",
            "null-engine/optional-args",
            "null-engine/ping",
            "null-engine/progress",
            "null-engine/slow",
            "null-engine/throw",
            "null-engine/unicode",
        };

        static readonly string[] ExpectedPromptNames =
        {
            "null-engine/greeting",
            "null-engine/static",
        };

        const string ConfigResourceUri = "null-engine://config";

        /// <summary>Duplicated from the engine's <c>Program.ReadyLinePrefix</c> - see the catalog remark above.</summary>
        const string ReadyLinePrefix = "NULL-ENGINE READY";

        /// <summary>Duplicated from <c>NullEngineTools.FailMessage</c> - see the catalog remark above.</summary>
        const string FailMessage = "null-engine: deliberate tool failure (fixed message).";

        /// <summary>Duplicated from <c>NullEngineTools.ThrowMessage</c>.</summary>
        const string ThrowMessage = "null-engine: deliberate unhandled exception (fixed message).";

        /// <summary>
        /// The exact wire text <c>null-engine/echo</c> answers for the payload below. The engine
        /// serializes its own response with an explicit, naming-policy-free serializer, so this is a
        /// byte-for-byte contract rather than a shape check.
        /// </summary>
        const string EchoExpectedText = "{\"result\":{\"Text\":\"round-trip\",\"Number\":7,\"Flag\":true}}";

        const string EchoText = "round-trip";
        const int EchoNumber = 7;

        /// <summary>
        /// CJK + Latin-1 + em dash + an astral-plane emoji, written as escapes on purpose.
        ///
        /// <para>Deliberately NOT the engine's own default sample - this one omits a character that
        /// one carries. A test that sent the engine its own default back could not tell an echo from
        /// a tool that ignored the argument and returned the default.</para>
        /// </summary>
        const string UnicodeText = "\u4F60\u597D \u00E9\u00E8\u00EA \u2014 \uD83D\uDE80";

        /// <summary>The prefixes the Microsoft.Extensions.Logging console formatter writes.</summary>
        static readonly string[] LogLevelPrefixes = { "trce:", "dbug:", "info:", "warn:", "fail:", "crit:" };

        /// <summary>
        /// The <c>AssemblyInformationalVersion</c> of the McpPlugin assembly THIS test process
        /// loaded. The engine loads its own copy of the same build output, so the two must agree —
        /// and that agreement is what the ready line's <c>plugin=</c> field is required to report.
        /// Read through the ProjectReference this csproj already carries, so no file-format parsing
        /// (and no platform-specific <c>FileVersionInfo</c> behaviour) enters the assertion.
        /// </summary>
        static readonly string McpPluginInformationalVersion =
            typeof(global::com.IvanMurzak.McpPlugin.McpPlugin).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? string.Empty;

        [Fact]
        public async Task RealPluginOverSignalR_ServesItsCatalog_AndSurvivesAToolThatThrows()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var engine = NullEngineProcess.Start(host.BaseUrl);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            // Strictly LONGER than the engine's own 30s handshake deadline (NullEngineProcess.Start's
            // default), so a slow handshake surfaces as the engine's own HANDSHAKE-FAILED diagnostic
            // rather than racing this wait to a less informative timeout.
            var ready = engine.WaitForReady(TimeSpan.FromSeconds(45));

            // ---- the ready contract -------------------------------------------------------------
            ready.GetProperty("pid").GetInt32().ShouldBe(engine.Id,
                "the ready file must describe the process this test spawned");
            ready.GetProperty("tools").GetInt32().ShouldBe(ExpectedToolNames.Length,
                "the ready file must report the engine's tool count");
            ready.GetProperty("prompts").GetInt32().ShouldBe(ExpectedPromptNames.Length,
                "the ready file must report the engine's prompt count");
            ready.GetProperty("resources").GetInt32().ShouldBe(1,
                "the ready file must report the engine's resource count");
            // TD ruling 2026-09-03: plugin= is the McpPlugin ASSEMBLY's AssemblyInformationalVersion
            // read at runtime — NEVER the Consts.PluginVersion compile-time constant. A presence
            // check ("not null or whitespace") cannot tell those two apart, so this asserts the exact
            // value against the same attribute read from the copy of McpPlugin.dll loaded here.
            // What makes it able to fail: swapping the host's runtime read for Consts.PluginVersion
            // reds this, because the two differ whenever the informational version carries
            // source-revision metadata — which every build from a git checkout produces
            // (measured on this tree: "8.3.0+de71a72…" vs "8.3.0").
            // NOT a null/blank check: the engine's resolver falls back to "unknown", so its reported
            // value is never blank and a blank-check here would be ENTAILED by the equality below
            // (zero halves, unfalsifiable). The condition that actually destroys discrimination is
            // the informational version collapsing ONTO the constant, which happens in any build
            // where no source-revision suffix is stamped (a source archive, a .git-less container
            // context). Assert THAT, so the day it happens this fails loudly instead of quietly
            // certifying a constant the ruling forbids.
            McpPluginInformationalVersion.ShouldNotBe(
                global::com.IvanMurzak.McpPlugin.Common.Consts.PluginVersion,
                "the McpPlugin assembly carries no source-revision suffix, so its runtime informational " +
                "version is byte-identical to Consts.PluginVersion and the equality below can no longer " +
                "tell the runtime read from the compile-time constant");
            ready.GetProperty("plugin_version").GetString().ShouldBe(McpPluginInformationalVersion,
                "the ready file must report the McpPlugin assembly's informational version, read at runtime");

            // A bounded WAIT, not an instant read. WaitForReady returns on the ready FILE's
            // existence, while the ready LINE reaches the capture through an asynchronous
            // OutputDataReceived callback, so sampling the list here races the capture thread on a
            // loaded runner. Same reasoning as the liveness window further down.
            var readyLine = engine.WaitForStdoutLine(ReadyLinePrefix, TimeSpan.FromSeconds(10));
            readyLine.ShouldNotBeNull("the engine must print a ready line to stdout. " + engine.Describe());
            engine.StdoutLines.Count(line => line.StartsWith(ReadyLinePrefix, StringComparison.Ordinal))
                .ShouldBe(1, "the engine must print EXACTLY one ready line, which is what the contract " +
                    "promises - an at-least-one check would pass on a host that printed it twice");
            // ONE exact equality rather than four substring checks: the ready line is a contract an
            // out-of-process consumer parses, and ShouldContain("resources=1") is satisfied by
            // "resources=10". Equality also gives the whole line a single plantable claim.
            readyLine.ShouldBe(
                $"{ReadyLinePrefix} tools={ExpectedToolNames.Length} prompts={ExpectedPromptNames.Length} " +
                $"resources=1 plugin={McpPluginInformationalVersion}",
                "the ready line is a contract, not a bag of substrings. " + engine.Describe());

            var sessionId = await host.HandshakeAsync(client, "null-engine-real-transport");

            // ---- tools/list: the EXACT set, not a superset ---------------------------------------
            var toolsBody = await host.CallAsync(client, sessionId, "tools/list", id: 2);
            using (var tools = JsonDocument.Parse(toolsBody))
            {
                var served = tools.RootElement
                    .GetProperty("result").GetProperty("tools")
                    .EnumerateArray()
                    .Select(tool => tool.GetProperty("name").GetString()!)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();

                served.ShouldBe(ExpectedToolNames,
                    "tools/list must serve exactly the engine's catalog: got [" + string.Join(", ", served) + "]");
            }

            // ---- tools/call echo: byte-for-byte round trip ---------------------------------------
            var echo = await CallToolAsync(host, client, sessionId, 3, "null-engine/echo", new
            {
                payload = new { text = EchoText, number = EchoNumber, flag = true }
            });
            echo.IsError.ShouldBeFalse("echo is a success path; got: " + echo.Text);
            echo.Text.ShouldBe(EchoExpectedText,
                "echo must round-trip the payload byte-for-byte");

            // ---- tools/call fail: a tool-level error, NOT a transport error ----------------------
            var failed = await CallToolAsync(host, client, sessionId, 4, "null-engine/fail", new { });
            failed.IsError.ShouldBeTrue("the fail tool must surface as isError:true");
            failed.Text.ShouldBe(FailMessage,
                "the fail tool's fixed message must reach the caller verbatim");

            // ---- tools/call throw: surfaced as an error, and the HOST STAYS ALIVE ----------------
            var threw = await CallToolAsync(host, client, sessionId, 5, "null-engine/throw", new { });
            threw.IsError.ShouldBeTrue("an exception inside a tool must surface as isError:true");
            threw.Text.ShouldContain(ThrowMessage, Case.Sensitive,
                "the surfaced error must carry the thrown message, not a generic transport failure");

            // A WINDOW, not an instant. Reading HasExited immediately after the reply cannot tell
            // "the engine survived" from "the engine has not finished dying yet": the error reply is
            // built and sent before any teardown the tool set off could complete, so an instant check
            // races the process it is about. Waiting bounds the claim instead of sampling it.
            engine.ExitedWithin(TimeSpan.FromSeconds(2)).ShouldBeFalse(
                "an exception inside a tool must NOT take the engine process down. " + engine.Describe());

            var pingAfterThrow = await CallToolAsync(host, client, sessionId, 6, "null-engine/ping", new { });
            pingAfterThrow.IsError.ShouldBeFalse("the session must still work after a throwing tool: " + pingAfterThrow.Text);
            pingAfterThrow.Text.ShouldBe("{\"result\":\"pong\"}",
                "ping must still answer normally after a throwing tool");

            // ---- tools/call unicode: non-ASCII survives the whole round trip ---------------------
            var unicode = await CallToolAsync(host, client, sessionId, 7, "null-engine/unicode", new { text = UnicodeText });
            unicode.IsError.ShouldBeFalse("unicode is a success path; got: " + unicode.Text);
            using (var parsed = JsonDocument.Parse(unicode.Text))
            {
                parsed.RootElement.GetProperty("result").GetProperty("text").GetString()
                    .ShouldBe(UnicodeText, "the non-ASCII payload must survive plugin -> hub -> server -> client");
            }

            // ---- prompts/list --------------------------------------------------------------------
            var promptsBody = await host.CallAsync(client, sessionId, "prompts/list", id: 8);
            using (var prompts = JsonDocument.Parse(promptsBody))
            {
                var served = prompts.RootElement
                    .GetProperty("result").GetProperty("prompts")
                    .EnumerateArray()
                    .Select(prompt => prompt.GetProperty("name").GetString()!)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();

                served.ShouldBe(ExpectedPromptNames,
                    "prompts/list must serve exactly the engine's prompts: got [" + string.Join(", ", served) + "]");
            }

            // ---- resources/read: the config document parses AND describes this run ---------------
            var (readBody, _) = await host.PostAsync(client, sessionId, new
            {
                jsonrpc = "2.0",
                id = 9,
                method = "resources/read",
                @params = new { uri = ConfigResourceUri }
            });
            using (var read = JsonDocument.Parse(NoneAuthMcpHost.Unwrap(readBody)))
            {
                var contents = read.RootElement.GetProperty("result").GetProperty("contents");
                contents.GetArrayLength().ShouldBe(1, "the config resource returns exactly one content block");

                var text = contents[0].GetProperty("text").GetString();
                text.ShouldNotBeNullOrWhiteSpace();

                using var config = JsonDocument.Parse(text!);
                config.RootElement.GetProperty("tools").GetInt32().ShouldBe(ExpectedToolNames.Length,
                    "the config resource must report the engine's tool count");
                config.RootElement.GetProperty("prompts").GetInt32().ShouldBe(ExpectedPromptNames.Length,
                    "the config resource must report the engine's prompt count");
                config.RootElement.GetProperty("resources").GetInt32().ShouldBe(1,
                    "the config resource must report the engine's resource count");
                config.RootElement.GetProperty("endpoint").GetString().ShouldBe(host.BaseUrl,
                    "the engine must report the endpoint this test actually pointed it at");
            }

            // ---- no 'fail:' line over the startup window ----------------------------------------
            // The seed this host replaces logs "Cannot resolve relative SkillsPath 'SKILLS' ...
            // ProjectRootPath is not set" at Error level during construction, which the console
            // formatter writes as a 'fail:' line BEFORE the ready line. The window is bounded at the
            // ready line rather than at the end of the run because two tools in the catalog
            // (fail / throw) are REQUIRED to produce errors, so an unbounded "zero fail: lines"
            // assertion could not hold for any run that calls them.
            var startupWindow = engine.StdoutLinesBeforeReady();
            startupWindow.Count.ShouldBeGreaterThan(0,
                "console logging must be captured at all, else the fail: assertion below is vacuous");
            startupWindow.Any(HasLogLevelPrefix).ShouldBeTrue(
                "the engine must log at Trace to stdout, else a fail: line could not appear even if it happened");
            startupWindow.Where(line => line.StartsWith("fail:", StringComparison.Ordinal)).ShouldBeEmpty(
                "the engine must reach ready without logging a single error. " + engine.Describe());

            // ---- every 'fail:' block after that is one of the two deliberate error tools ---------
            var failBlocks = engine.FailBlocks();
            // One floor PER deliberate tool, not a combined count of two: this test called fail AND
            // throw, and a bare 'at least two' is satisfied by two blocks from the SAME tool, which
            // would hide a regression that silenced the other one. Both floors together also rule
            // out the empty set that would satisfy the attribution loop below for free.
            failBlocks.Count(block => block.Contains(FailMessage, StringComparison.Ordinal))
                .ShouldBeGreaterThan(0, "null-engine/fail must log the error block it advertises. " + engine.Describe());
            failBlocks.Count(block => block.Contains(ThrowMessage, StringComparison.Ordinal))
                .ShouldBeGreaterThan(0, "null-engine/throw's exception must reach the engine's own error log. " + engine.Describe());
            foreach (var block in failBlocks)
            {
                (block.Contains(FailMessage, StringComparison.Ordinal) || block.Contains(ThrowMessage, StringComparison.Ordinal))
                    .ShouldBeTrue("unexpected error logged by the engine:\n" + block);
            }

            // ---- teardown: the engine is killed, leaving no orphan --------------------------------
            // Asserted HERE, on the engine that actually connected and served this whole session —
            // that is the shape a leaked process would come from, and it is still ALIVE at this point
            // (the liveness assertion above proved so). Removing the Kill() call below reds this,
            // which is what makes the assertion a claim about killing rather than about a process that
            // had already died on its own.
            engine.Kill();
            engine.HasExited.ShouldBeTrue(
                "teardown must leave no orphan engine process behind. " + engine.Describe());
        }

        /// <summary>
        /// The other half of the CLI contract every out-of-process consumer of this engine keys off:
        /// an engine that cannot complete the version handshake inside <c>--mcp-server-timeout</c>
        /// exits <b>3</b> and says so on stderr — it does not hang, and it does not exit 0.
        /// Port 1 is used because nothing can be listening there.
        /// </summary>
        [Fact]
        public void HandshakeFailure_ExitsWithCode3_AndLeavesNoOrphan()
        {
            using var engine = NullEngineProcess.Start("http://127.0.0.1:1", timeoutMs: 2000);

            engine.ExitedWithin(TimeSpan.FromSeconds(60)).ShouldBeTrue(
                "an unreachable endpoint must END the process, not hang it. " + engine.Describe());
            engine.ExitCode.ShouldBe(3,
                "a failed handshake must exit 3, not 0. " + engine.Describe());
            engine.StderrLines.Any(line => line.StartsWith("NULL-ENGINE HANDSHAKE-FAILED", StringComparison.Ordinal))
                .ShouldBeTrue("the failure must be reported on stderr. " + engine.Describe());
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static bool HasLogLevelPrefix(string line)
            => LogLevelPrefixes.Any(prefix => line.StartsWith(prefix, StringComparison.Ordinal));

        static async Task<(bool IsError, string Text)> CallToolAsync(
            NoneAuthMcpHost host, HttpClient client, string sessionId, int id, string name, object arguments)
        {
            var (body, _) = await host.PostAsync(client, sessionId, new
            {
                jsonrpc = "2.0",
                id,
                method = "tools/call",
                @params = new { name, arguments }
            });

            using var document = JsonDocument.Parse(NoneAuthMcpHost.Unwrap(body));
            var result = document.RootElement.GetProperty("result");

            var isError = result.TryGetProperty("isError", out var isErrorElement) && isErrorElement.GetBoolean();
            var text = string.Empty;
            if (result.TryGetProperty("content", out var content))
            {
                foreach (var block in content.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var type) && type.GetString() == "text")
                    {
                        text = block.GetProperty("text").GetString() ?? string.Empty;
                        break;
                    }
                }
            }

            return (isError, text);
        }

        /// <summary>
        /// A running <c>dotnet McpPlugin.NullEngine.dll ...</c> subprocess with line-buffered output
        /// capture, following the <c>McpPlugin.Tests.LockHarness</c> precedent.
        /// </summary>
        sealed class NullEngineProcess : IDisposable
        {
            readonly Process _process;
            readonly List<string> _stdout = new();
            readonly List<string> _stderr = new();
            readonly object _gate = new();
            readonly string _workDirectory;
            readonly string _readyFile;

            NullEngineProcess(ProcessStartInfo startInfo, string workDirectory, string readyFile)
            {
                _workDirectory = workDirectory;
                _readyFile = readyFile;
                _process = new Process { StartInfo = startInfo };
                _process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data == null)
                        return;
                    lock (_gate)
                        _stdout.Add(e.Data);
                };
                _process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data == null)
                        return;
                    lock (_gate)
                        _stderr.Add(e.Data);
                };
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }

            public static NullEngineProcess Start(string endpoint, int timeoutMs = 30000)
            {
                var engineDll = ResolveEngineDll();
                File.Exists(engineDll).ShouldBeTrue(
                    "the null engine was not found at " + engineDll +
                    " - McpPlugin.NullEngine must be part of McpPlugin.sln so a solution build produces it, " +
                    "or set MCPPLUGIN_NULLENGINE_DLL to an explicit path");

                var workDirectory = Path.Combine(Path.GetTempPath(), "null-engine-test-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workDirectory);
                var readyFile = Path.Combine(workDirectory, "ready.json");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = AppContext.BaseDirectory,
                };
                // The child inherits this process's environment, and ConnectionConfig reads
                // MCP_SKILLS_FOLDER / MCP_SERVER_ENDPOINT / MCP_SERVER_TIMEOUT from it. The first
                // feeds the SkillsPath resolution whose failure IS the `fail:` line the startup-window
                // assertion polices, so an ambient value could red this test for a purely
                // environmental reason. Every one of them is passed explicitly below; drop the
                // inherited copies so the arguments are the only input.
                startInfo.Environment.Remove("MCP_SKILLS_FOLDER");
                startInfo.Environment.Remove("MCP_SERVER_ENDPOINT");
                startInfo.Environment.Remove("MCP_SERVER_TIMEOUT");

                startInfo.ArgumentList.Add(engineDll);
                startInfo.ArgumentList.Add("--mcp-server-endpoint=" + endpoint);
                startInfo.ArgumentList.Add("--project-root=" + Path.Combine(workDirectory, "project"));
                startInfo.ArgumentList.Add("--ready-file=" + readyFile);
                startInfo.ArgumentList.Add(
                    "--mcp-server-timeout=" + timeoutMs.ToString(CultureInfo.InvariantCulture));

                return new NullEngineProcess(startInfo, workDirectory, readyFile);
            }

            /// <summary>
            /// <c>AppContext.BaseDirectory</c> is
            /// <c>&lt;repo&gt;/McpPlugin.Server.Tests/bin/&lt;Configuration&gt;/&lt;tfm&gt;/</c>, so the
            /// engine built for the SAME configuration and the SAME target framework as this test leg
            /// sits four directories up. Deriving the path rather than adding a ProjectReference is
            /// NOT about assembly isolation — this csproj already references <c>McpPlugin</c>, so
            /// that graph is loaded here regardless (see the class remarks). It avoids a build-order
            /// coupling and keeps the engine's outputs from being copied beside this test's.
            /// </summary>
            static string ResolveEngineDll()
            {
                var explicitPath = Environment.GetEnvironmentVariable("MCPPLUGIN_NULLENGINE_DLL");
                if (!string.IsNullOrWhiteSpace(explicitPath))
                    return Path.GetFullPath(explicitPath!);

                var testBin = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                var targetFramework = testBin.Name;
                var configuration = testBin.Parent?.Name ?? "Release";
                var repositoryRoot = testBin.Parent?.Parent?.Parent?.Parent?.FullName
                    ?? Directory.GetCurrentDirectory();

                return Path.Combine(repositoryRoot, "McpPlugin.NullEngine", "bin", configuration, targetFramework,
                    "McpPlugin.NullEngine.dll");
            }

            public int Id => _process.Id;
            public bool HasExited => _process.HasExited;
            public int ExitCode => _process.ExitCode;

            public IReadOnlyList<string> StderrLines
            {
                get
                {
                    lock (_gate)
                        return _stderr.ToArray();
                }
            }

            /// <summary>
            /// True when the process exits within <paramref name="window"/>, false when it is still
            /// running at the end of it.
            ///
            /// <para>The parameterless <c>WaitForExit()</c> after a positive result is load-bearing,
            /// not belt-and-braces: the timed overload returns as soon as the process object reports
            /// exit, WITHOUT waiting for the asynchronous stdout/stderr handlers to drain. A caller
            /// that asserts on captured output right afterwards - as the handshake-failure test does
            /// with the stderr line the engine writes microseconds before exiting - would otherwise
            /// read a list the capture thread has not finished filling.</para>
            /// </summary>
            public bool ExitedWithin(TimeSpan window)
            {
                if (!_process.WaitForExit((int)window.TotalMilliseconds))
                    return false;

                _process.WaitForExit();
                return true;
            }

            /// <summary>
            /// Waits up to <paramref name="timeout"/> for a captured stdout line starting with
            /// <paramref name="prefix"/>, or null if none arrives. Needed because the capture is
            /// asynchronous, so a line the engine has already written may not be in the list yet.
            /// </summary>
            public string? WaitForStdoutLine(string prefix, TimeSpan timeout)
            {
                var deadlineUtc = DateTime.UtcNow + timeout;
                while (true)
                {
                    var hit = StdoutLines.FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
                    if (hit != null)
                        return hit;

                    if (DateTime.UtcNow >= deadlineUtc)
                        return null;

                    Thread.Sleep(25);
                }
            }

            public IReadOnlyList<string> StdoutLines
            {
                get
                {
                    lock (_gate)
                        return _stdout.ToArray();
                }
            }

            /// <summary>
            /// The stdout captured BEFORE the ready line - the engine's whole startup window, which
            /// is where a skill-path / configuration error would be logged.
            /// </summary>
            public IReadOnlyList<string> StdoutLinesBeforeReady()
            {
                var lines = StdoutLines;
                for (var i = 0; i < lines.Count; i++)
                {
                    if (lines[i].StartsWith(ReadyLinePrefix, StringComparison.Ordinal))
                        return lines.Take(i).ToArray();
                }

                // No ready line captured means there IS no startup window to return. Falling back to
                // the whole capture would fold the deliberate fail/throw blocks into it and red the
                // startup assertion with a message blaming the engine for an error it never logged.
                throw new Xunit.Sdk.XunitException(
                    "no ready line was captured, so the startup window is undefined. " + Describe());
            }

            /// <summary>
            /// Every <c>fail:</c> line plus the indented continuation lines the console formatter
            /// writes under it, which is where the message itself lives.
            /// </summary>
            public IReadOnlyList<string> FailBlocks()
            {
                var lines = StdoutLines;
                var blocks = new List<string>();
                for (var i = 0; i < lines.Count; i++)
                {
                    if (!lines[i].StartsWith("fail:", StringComparison.Ordinal))
                        continue;

                    var block = new StringBuilder(lines[i]);
                    for (var j = i + 1; j < lines.Count && !HasLogLevelPrefix(lines[j]); j++)
                        block.Append('\n').Append(lines[j]);

                    blocks.Add(block.ToString());
                }
                return blocks;
            }

            public JsonElement WaitForReady(TimeSpan timeout)
            {
                var deadlineUtc = DateTime.UtcNow + timeout;
                while (DateTime.UtcNow < deadlineUtc)
                {
                    if (File.Exists(_readyFile))
                    {
                        using var document = JsonDocument.Parse(File.ReadAllText(_readyFile));
                        return document.RootElement.Clone();
                    }

                    if (_process.HasExited)
                        break;

                    Thread.Sleep(100);
                }

                throw new Xunit.Sdk.XunitException(
                    "the null engine never became ready within " + timeout + ". " + Describe());
            }

            public void Kill()
            {
                if (_process.HasExited)
                    return;

                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(15000);
            }

            /// <summary>
            /// Built from the snapshot accessors rather than under <c>_gate</c>: that is the same lock
            /// the output callbacks take to append lines, and every assertion message here is built
            /// EAGERLY (Shouldly 4.x has no lazy message overload), including on the passing path.
            /// Holding it across two joins of a Trace-level capture would briefly stall stdout
            /// draining for the very process the assertion is about - and the liveness check calls
            /// this while the engine is still running.
            /// </summary>
            public string Describe()
            {
                var exited = _process.HasExited
                    ? _process.ExitCode.ToString(CultureInfo.InvariantCulture)
                    : "no";

                return "exited=" + exited +
                    "\nstdout:\n" + string.Join("\n", StdoutLines) +
                    "\nstderr:\n" + string.Join("\n", StderrLines);
            }

            public void Dispose()
            {
                // Both catches are deliberately broad. This runs while a `using` may be unwinding an
                // assertion failure, and an exception thrown out of Dispose REPLACES that failure -
                // turning a precise red into an unrelated teardown stack trace, which is the worst
                // outcome on a CI-only failure. Kill(entireProcessTree) is documented to throw
                // AggregateException when a child races it, and Directory.Delete throws
                // UnauthorizedAccessException (NOT an IOException) when a just-killed process still
                // holds a handle. Neither is worth losing the real failure over.
                try
                {
                    Kill();
                }
                catch (Exception)
                {
                    // Already gone, or the OS refused - teardown is best effort.
                }
                finally
                {
                    _process.Dispose();
                    try
                    {
                        Directory.Delete(_workDirectory, recursive: true);
                    }
                    catch (Exception)
                    {
                        // Best effort - the OS temp directory is swept anyway.
                    }
                }
            }
        }
    }
}
