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
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Chain.Testing;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests.Chain
{
    /// <summary>
    /// The T2 record / replay contract, end to end: a real server host
    /// (<see cref="NoneAuthMcpHost"/>), the real <c>McpPlugin.NullEngine</c> process over the real
    /// SignalR transport, and the real <c>scripts/chain_fixture.py</c>.
    ///
    /// <para>Every assertion here is about the FORMAT, not about the null engine: the engine is
    /// the one deterministic plugin this repository can drive, and the same recorder runs against
    /// Unity / Godot / Unreal in their own repositories.</para>
    ///
    /// <para><b>One plugin at a time.</b> <c>NoAuthMcpStrategy</c> allows a single plugin
    /// connection and the client registry is process-global, so a test needing two engines runs
    /// them sequentially, each with its OWN host, and the class joins the <c>McpPlugin.Server</c>
    /// collection so it is serialised against the other classes in it.</para>
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class ChainRecordReplayTests : IDisposable
    {
        /// <summary>
        /// The engine's hand-written catalogue, duplicated rather than referenced — this project
        /// deliberately does not reference the engine assembly, and a coordinated rename on both
        /// sides must break this list. Same reasoning as <c>NullEngineRealTransportTests</c>.
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

        /// <summary>Calls in <c>tests/chain-fixtures/null-engine/battery.json</c>.</summary>
        const int BatteryCallCount = 14;

        static readonly TimeSpan EngineStartTimeout = TimeSpan.FromSeconds(90);

        readonly string _scratch = ChainToolchain.NewScratchDirectory();

        public void Dispose()
        {
            try
            {
                Directory.Delete(_scratch, recursive: true);
            }
            catch (Exception)
            {
                // Best effort - the OS temp directory is swept anyway.
            }
        }

        // ───────────────────────────── DoD 2 ─────────────────────────────

        [PythonFact]
        public async Task RecordOverMcp_ProducesAFixtureCarryingTheHandWrittenCatalog()
        {
            var fixture = Path.Combine(_scratch, "recorded.jsonl");
            await RecordAgainstLiveEngineAsync(fixture, ChainToolchain.Battery);

            var lines = ChainToolchain.ReadFixtureLines(fixture);
            var meta = Parse(lines[0]);
            meta.GetProperty("kind").GetString().ShouldBe("meta");
            meta.GetProperty("schema").GetInt32().ShouldBe(1);
            meta.GetProperty("recorder").GetString().ShouldBe("mcp-client",
                "a fixture taken over /mcp must say so - the recorder kind is what --cross-recorder keys on");
            meta.GetProperty("engine").GetString().ShouldBe("null-engine");
            meta.GetProperty("surface").GetString().ShouldBe("editor");

            var toolNames = LinesOfKind(lines, "tool").Select(NameOf).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            toolNames.ShouldBe(ExpectedToolNames,
                "record must capture EXACTLY the engine's catalogue: got [" + string.Join(", ", toolNames) + "]");

            var calls = LinesOfKind(lines, "call").ToList();
            calls.Count.ShouldBe(BatteryCallCount, "every battery entry must produce one call line");
            foreach (var call in calls)
            {
                call.GetProperty("args_hash").GetString().ShouldNotBeNullOrEmpty();
                call.GetProperty("response").TryGetProperty("status", out _).ShouldBeTrue(
                    "every recorded response carries a status");
            }

            // The battery calls null-engine/optional-args TWICE with different arguments. Without
            // two DIFFERENT hashes under one name, nothing in this suite could tell a lookup keyed
            // on the name from one keyed on (name, args_hash) - which is what F6 promises.
            var optionalArgsHashes = calls
                .Where(call => NameOf(call) == "null-engine/optional-args")
                .Select(call => call.GetProperty("args_hash").GetString())
                .Distinct()
                .ToList();
            optionalArgsHashes.Count.ShouldBe(2,
                "the reference battery must exercise two distinct argument sets for one tool");
        }

        // ───────────────────────────── DoD 3 ─────────────────────────────

        [PythonFact]
        public async Task InProcessDumpAndMcpRecord_AgreeAfterCanonicalization()
        {
            // (a) the MCP-client capture: a real server, a real engine, a real transport.
            var overMcp = Path.Combine(_scratch, "mcp.jsonl");
            await RecordAgainstLiveEngineAsync(overMcp, ChainToolchain.Battery);

            // (b) the in-process capture: the SAME battery, no server at all.
            var raw = Path.Combine(_scratch, "raw.jsonl");
            using (var engine = ChainEngineProcess.Start(
                "--dump-raw=" + raw,
                "--battery=" + ChainToolchain.Battery,
                "--engine=null-engine",
                "--engine-version=0",
                "--surface=editor"))
            {
                engine.ExitedWithin(EngineStartTimeout).ShouldBeTrue(
                    "--dump-raw must finish and exit, not connect and wait. " + engine.Describe());
                engine.ExitCode.ShouldBe(0, "--dump-raw must exit 0. " + engine.Describe());
                engine.WaitForStdoutLine(ChainEngineProcess.DumpRawLinePrefix, TimeSpan.FromSeconds(5))
                    .ShouldNotBeNull("--dump-raw must report what it wrote. " + engine.Describe());
            }

            var inProcess = Path.Combine(_scratch, "in-process.jsonl");
            Run("canonicalize", raw, "--out", inProcess);

            ChainToolchain.ReadFixtureLines(inProcess).Count
                .ShouldBe(ChainToolchain.ReadFixtureLines(overMcp).Count,
                    "both recorders must produce the same number of lines");

            // POSITIVE HALF FIRST. The comparison below neutralises the two recorder-scoped fields,
            // so it would also pass if the two files were identical for an uninteresting reason -
            // both empty, say, or both recorded the same way. Prove they are NOT: the strict diff
            // must FAIL, and on exactly those fields.
            var strict = RunExpecting(3, "diff", inProcess, overMcp);
            Contains(strict.StdOut, "\"recorder\":\"in-process\"",
                "the strict diff must show the recorder kind differing. " + strict.Describe());
            Contains(strict.StdOut, "errorKind",
                "the strict diff must show errorKind, which only the in-process recorder can observe. " + strict.Describe());

            var inProcessComparable = Path.Combine(_scratch, "in-process.x.jsonl");
            var overMcpComparable = Path.Combine(_scratch, "mcp.x.jsonl");
            Run("canonicalize", inProcess, "--for-diff", "--cross-recorder", "--out", inProcessComparable);
            Run("canonicalize", overMcp, "--for-diff", "--cross-recorder", "--out", overMcpComparable);

            ChainToolchain.ReadText(inProcessComparable).ShouldBe(
                ChainToolchain.ReadText(overMcpComparable),
                "the in-process recorder and the MCP-client recorder must produce byte-identical " +
                "fixtures once the fields determined by the recorder KIND are neutralised. A " +
                "difference here means RawDump's mirror of ToolRouter.Call / ToCallToolResult / " +
                "ToContent has drifted from the server.");

            Run("diff", inProcess, overMcp, "--cross-recorder");
        }

        // ───────────────────────────── DoD 4 ─────────────────────────────

        [PythonFact]
        public async Task Replay_RoundTripsByteIdentically_AndServesTheFixturesToolCount()
        {
            var recorded = Path.Combine(_scratch, "f1.jsonl");
            await RecordAgainstLiveEngineAsync(recorded, ChainToolchain.Battery);
            var fixtureToolCount = ChainToolchain.ReadFixtureLines(recorded).Count(line => KindOf(line) == "tool");

            var replayed = Path.Combine(_scratch, "f3.jsonl");
            await using (var host = await NoneAuthMcpHost.StartAsync())
            using (var engine = ChainEngineProcess.StartConnected(host.BaseUrl, "--replay=" + recorded))
            {
                var replayLine = engine.WaitForStdoutLine(ChainEngineProcess.ReplayLinePrefix, EngineStartTimeout);
                replayLine.ShouldNotBeNull("a --replay run must report the fixture it loaded. " + engine.Describe());
                Contains(replayLine!, "registered=" + fixtureToolCount,
                    "one ReplayTool per fixture tool line. " + engine.Describe());

                var ready = engine.WaitForReady(EngineStartTimeout);
                ChainEngineProcess.ToolsFromReadyLine(ready).ShouldBe(fixtureToolCount,
                    "in --replay the host must publish the FIXTURE's catalogue, not the built-in one. " + engine.Describe());

                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                var sessionId = await host.HandshakeAsync(client, "chain-replay");
                var served = await ListToolNamesAsync(host, client, sessionId);
                served.Length.ShouldBe(fixtureToolCount,
                    "consumer-side tools/list must equal the fixture's tool count: got [" + string.Join(", ", served) + "]");
                served.ShouldBe(ExpectedToolNames,
                    "the replayed catalogue must be the recorded one, name for name");

                Run("record",
                    "--server-url", host.BaseUrl,
                    "--battery", ChainToolchain.Battery,
                    "--engine", "null-engine",
                    "--engine-version", "0",
                    "--surface", "editor",
                    "--out", replayed);
            }

            // A replay that missed anything answers with a replay-* error text, so this is the
            // observation that makes the byte comparison below a claim about FIDELITY rather than
            // about two files that merely agree.
            ChainToolchain.ReadText(replayed).Contains("replay-miss", StringComparison.Ordinal).ShouldBeFalse(
                "a faithful replay of its own battery must not miss anything: " + replayed);

            Run("diff", recorded, replayed);

            var recordedComparable = Path.Combine(_scratch, "f1.d.jsonl");
            var replayedComparable = Path.Combine(_scratch, "f3.d.jsonl");
            Run("canonicalize", recorded, "--for-diff", "--out", recordedComparable);
            Run("canonicalize", replayed, "--for-diff", "--out", replayedComparable);
            ChainToolchain.ReadText(replayedComparable).ShouldBe(
                ChainToolchain.ReadText(recordedComparable),
                "recording a REPLAY of a fixture must reproduce that fixture byte for byte");
        }

        // ───────────────── DoD 5 (d) (e) — replay never succeeds silently ─────────────────

        [PythonFact]
        public async Task Replay_NamesEveryMiss_AndAnOversizeResponseIsNeverServed()
        {
            // battery-oversize.json calls null-engine/large-payload with kb=40, whose response is
            // far above F4's 32 KB cap, plus a small call beside it.
            var oversize = Path.Combine(_scratch, "oversize.jsonl");
            await RecordAgainstLiveEngineAsync(oversize, ChainToolchain.OversizeBattery);

            var lines = ChainToolchain.ReadFixtureLines(oversize).Select(Parse).ToList();
            var large = lines.Single(line => KindOf(line) == "call" && NameOf(line) == "null-engine/large-payload");
            var largeResponse = large.GetProperty("response");
            largeResponse.TryGetProperty("$hash", out var hashProperty).ShouldBeTrue(
                "a response over the 32 KB cap must be stored as its digest");
            var digest = hashProperty.GetString()!;
            digest.StartsWith("sha256:", StringComparison.Ordinal).ShouldBeTrue("the digest names its algorithm: " + digest);
            largeResponse.GetProperty("$bytes").GetInt32().ShouldBeGreaterThan(32 * 1024,
                "the recorded byte count must be the size that tripped the cap");

            // The positive half: the SAME fixture carries an un-capped response, so "the big one
            // was replaced by a digest" is a statement about the cap and not about the recorder.
            var small = lines.Single(line => KindOf(line) == "call" && NameOf(line) == "null-engine/ping");
            small.GetProperty("response").TryGetProperty("$hash", out _).ShouldBeFalse(
                "a small response must be stored in full");
            small.GetProperty("response").GetProperty("content").GetArrayLength().ShouldBe(1);

            // A digest still discriminates: change it and the diff reports a contract change.
            var tampered = Path.Combine(_scratch, "oversize-tampered.jsonl");
            ChainToolchain.WriteText(tampered, ChainToolchain.ReadText(oversize)
                .Replace(digest, "sha256:" + new string('0', 64), StringComparison.Ordinal));
            RunExpecting(3, "diff", oversize, tampered);

            await using var host = await NoneAuthMcpHost.StartAsync();
            using var engine = ChainEngineProcess.StartConnected(host.BaseUrl, "--replay=" + oversize);
            engine.WaitForReady(EngineStartTimeout);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var sessionId = await host.HandshakeAsync(client, "chain-replay-miss");

            // (1) a capped response cannot be served - and is never served as something else.
            var capped = await CallToolAsync(host, client, sessionId, 20, "null-engine/large-payload", new { kb = 40 });
            capped.IsError.ShouldBeTrue("a capped response must surface as an error at the MCP client");
            StartsWith(capped.Text, "replay-oversize: ", "got: " + capped.Text);
            Contains(capped.Text, digest, "the error must name the digest so a consumer can find the recorded entry");

            // (2) a tool the fixture publishes but never exercised.
            var missed = await CallToolAsync(host, client, sessionId, 21, "null-engine/echo", new { });
            missed.IsError.ShouldBeTrue("an unrecorded tool must surface as isError:true at the MCP client");
            missed.Text.ShouldBe("replay-miss: null-engine/echo");

            // (3) a recorded tool called with arguments the fixture does not carry.
            var wrongArgs = await CallToolAsync(host, client, sessionId, 22, "null-engine/ping", new { unexpected = 1 });
            wrongArgs.IsError.ShouldBeTrue("unknown arguments must surface as isError:true");
            StartsWith(wrongArgs.Text, "replay-miss-args: null-engine/ping sha256:", "got: " + wrongArgs.Text);

            // (4) and the recorded call still works, so none of the above says "replay is broken".
            var hit = await CallToolAsync(host, client, sessionId, 23, "null-engine/ping", new { });
            hit.IsError.ShouldBeFalse("the recorded call must still replay: " + hit.Text);
            hit.Text.ShouldBe("{\"result\":\"pong\"}");
        }

        // ───────────── DoD 5 (a) (b) — what a diff does and does not report ─────────────

        [PythonFact]
        public void Diff_ReportsAChangedResponse_AndIgnoresOnlyTheProvenanceMeta()
        {
            var committed = ChainToolchain.ReferenceFixture;
            File.Exists(committed).ShouldBeTrue("the reference fixture must be committed at " + committed);

            // (a) a changed response value IS a contract change. The literal below is the ping
            // response as it appears INSIDE the content block's JSON string, so the mutation
            // touches one recorded value and nothing structural.
            var original = ChainToolchain.ReadText(committed);
            Contains(original, "\\\"result\\\":\\\"pong\\\"",
                "the reference fixture must carry the ping response this mutation targets, else the " +
                "assertion below is vacuous");
            var changedResponse = Path.Combine(_scratch, "changed-response.jsonl");
            ChainToolchain.WriteText(changedResponse,
                original.Replace("\\\"result\\\":\\\"pong\\\"", "\\\"result\\\":\\\"PONG\\\"", StringComparison.Ordinal));
            var responseDiff = RunExpecting(3, "diff", committed, changedResponse);
            Contains(responseDiff.StdOut, "contract-change", responseDiff.Describe());
            Contains(responseDiff.StdOut, "null-engine/ping",
                "the diff must name the line that changed. " + responseDiff.Describe());

            // (b) the provenance meta fields are NOT.
            Run("diff", committed, WithMetaField(committed, "recorded_at", "2099-01-01T00:00:00Z"));
            Run("diff", committed, WithMetaField(committed, "mcp_plugin_version", "8.3.0-ws.gdeadbee"));
            Run("diff", committed, WithMetaField(committed, "plugin_version", "0.0.0-ws.gdeadbee"));

            // The positive half of (b): the meta LINE is compared, so a NON-provenance meta field
            // still reddens. Without this, "exit 0" above would be equally well explained by a diff
            // that ignores the meta line altogether.
            RunExpecting(3, "diff", committed, WithMetaField(committed, "surface", "runtime"));
            RunExpecting(3, "diff", committed, WithMetaField(committed, "engine_version", "9999"));
        }

        // ───────────────────────── F4 / F5 refusals ─────────────────────────

        [PythonFact]
        public void Canonicalize_RefusesAFixtureOverTheCap_AndAnUnknownSchema()
        {
            // Just under the cap: canonicalises fine. This is the control - without it, the exit 4
            // below would be equally well explained by the generator producing something unreadable.
            var small = Path.Combine(_scratch, "small.jsonl");
            ChainToolchain.WriteText(small, SyntheticFixture(padCalls: 6, padBytes: 30 * 1024));
            Run("canonicalize", small, "--out", Path.Combine(_scratch, "small.out.jsonl"));

            var big = Path.Combine(_scratch, "big.jsonl");
            ChainToolchain.WriteText(big, SyntheticFixture(padCalls: 12, padBytes: 30 * 1024));
            var refused = RunExpecting(4, "canonicalize", big, "--out", Path.Combine(_scratch, "big.out.jsonl"));
            Contains(refused.StdErr, "fixture too large: trim the battery",
                "the cap must say what to do about it. " + refused.Describe());

            var wrongSchema = Path.Combine(_scratch, "schema2.jsonl");
            ChainToolchain.WriteText(wrongSchema,
                "{\"schema\":2,\"kind\":\"meta\",\"engine\":\"x\",\"engine_version\":\"0\"," +
                "\"surface\":\"editor\",\"recorder\":\"mcp-client\"}\n");
            var mismatch = RunExpecting(4, "canonicalize", wrongSchema);
            Contains(mismatch.StdErr, "schema mismatch", mismatch.Describe());

            var diffMismatch = RunExpecting(4, "diff", ChainToolchain.ReferenceFixture, wrongSchema);
            Contains(diffMismatch.StdErr, "schema mismatch", diffMismatch.Describe());
        }

        // ───────────────── the committed reference sample ─────────────────

        [PythonFact]
        public void ReferenceFixture_IsAlreadyCanonical_AndCoversTheWholeCatalogue()
        {
            var committed = ChainToolchain.ReferenceFixture;
            var recanonicalized = Path.Combine(_scratch, "recanonicalized.jsonl");
            Run("canonicalize", committed, "--out", recanonicalized);

            ChainToolchain.ReadText(recanonicalized).ShouldBe(ChainToolchain.ReadText(committed),
                "the committed fixture must already be in canonical form - canonicalising it being " +
                "a no-op is what lets `diff` compare a committed file against a fresh one");

            ChainToolchain.ReadText(committed).Contains('\r').ShouldBeFalse(
                "F1 fixes the line ending at LF; a CRLF checkout would break every byte comparison " +
                "on Windows (see .gitattributes)");

            var lines = ChainToolchain.ReadFixtureLines(committed);
            LinesOfKind(lines, "tool").Select(NameOf).OrderBy(n => n, StringComparer.Ordinal).ToArray()
                .ShouldBe(ExpectedToolNames, "the reference fixture must cover the whole catalogue");

            var calledNames = LinesOfKind(lines, "call").Select(NameOf).Distinct()
                .OrderBy(n => n, StringComparer.Ordinal).ToArray();
            calledNames.ShouldBe(ExpectedToolNames,
                "every tool in the catalogue must be exercised by the reference battery, else the " +
                "fixture silently covers a subset of the contract");
        }

        // ───────────────────────────── helpers ─────────────────────────────

        async Task RecordAgainstLiveEngineAsync(string outPath, string batteryPath)
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var engine = ChainEngineProcess.StartConnected(host.BaseUrl);
            engine.WaitForReady(EngineStartTimeout);

            Run("record",
                "--server-url", host.BaseUrl,
                "--battery", batteryPath,
                "--engine", "null-engine",
                "--engine-version", "0",
                "--surface", "editor",
                "--out", outPath);

            File.Exists(outPath).ShouldBeTrue("record must write " + outPath + ". " + engine.Describe());
        }

        static ChainToolchain.ProcessResult Run(params string[] arguments) => RunExpecting(0, arguments);

        static ChainToolchain.ProcessResult RunExpecting(int expectedExit, params string[] arguments)
        {
            var result = ChainToolchain.RunScript(arguments);
            result.ExitCode.ShouldBe(expectedExit,
                "chain_fixture.py " + string.Join(" ", arguments) + "\n" + result.Describe());
            return result;
        }

        static void Contains(string actual, string expected, string because)
            => actual.Contains(expected, StringComparison.Ordinal).ShouldBeTrue(
                "expected to contain '" + expected + "'. " + because);

        static void StartsWith(string actual, string expected, string because)
            => actual.StartsWith(expected, StringComparison.Ordinal).ShouldBeTrue(
                "expected to start with '" + expected + "'. " + because);

        /// <summary>A copy of <paramref name="path"/> with one meta field replaced.</summary>
        string WithMetaField(string path, string field, string value)
        {
            var lines = ChainToolchain.ReadFixtureLines(path);
            var meta = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(lines[0])!;
            meta.ContainsKey(field).ShouldBeTrue(
                "the fixture must already carry meta." + field + ", else this mutation changes " +
                "nothing and the assertion built on it is vacuous");
            meta[field] = JsonSerializer.SerializeToElement(value);
            lines[0] = JsonSerializer.Serialize(meta);

            var target = Path.Combine(_scratch, "meta-" + field + ".jsonl");
            ChainToolchain.WriteText(target, string.Join("\n", lines) + "\n");
            return target;
        }

        /// <summary>
        /// A schema-1 fixture with <paramref name="padCalls"/> calls of roughly
        /// <paramref name="padBytes"/> each — each one UNDER the 32 KB response cap, so the total
        /// crosses the 256 KB fixture cap without any single response being hashed away first.
        /// </summary>
        static string SyntheticFixture(int padCalls, int padBytes)
        {
            var lines = new List<string>
            {
                "{\"schema\":1,\"kind\":\"meta\",\"engine\":\"synthetic\",\"engine_version\":\"0\"," +
                "\"surface\":\"editor\",\"recorder\":\"mcp-client\",\"recorded_at\":\"2026-01-01T00:00:00Z\"}",
                "{\"kind\":\"tool\",\"name\":\"synthetic/pad\",\"inputSchema\":{\"type\":\"object\"}}",
            };
            var payload = new string('p', padBytes);
            for (var i = 0; i < padCalls; i++)
            {
                lines.Add(
                    "{\"kind\":\"call\",\"name\":\"synthetic/pad\",\"args\":{\"i\":" + i + "}," +
                    "\"response\":{\"status\":\"success\",\"content\":[{\"type\":\"text\",\"text\":\"" + payload + "\"}]}}");
            }
            return string.Join("\n", lines) + "\n";
        }

        static JsonElement Parse(string line)
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }

        static string KindOf(string line) => Parse(line).GetProperty("kind").GetString()!;

        static string KindOf(JsonElement line) => line.GetProperty("kind").GetString()!;

        static string NameOf(JsonElement line) => line.GetProperty("name").GetString()!;

        static IEnumerable<JsonElement> LinesOfKind(IEnumerable<string> lines, string kind)
            => lines.Select(Parse).Where(line => KindOf(line) == kind);

        static async Task<string[]> ListToolNamesAsync(NoneAuthMcpHost host, HttpClient client, string sessionId)
        {
            var body = await host.CallAsync(client, sessionId, "tools/list", id: 2);
            using var document = JsonDocument.Parse(body);
            return document.RootElement
                .GetProperty("result").GetProperty("tools")
                .EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString()!)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }

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
    }
}
