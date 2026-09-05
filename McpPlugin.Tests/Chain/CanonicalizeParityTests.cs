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
using System.Text;
using System.Text.Json;
using com.IvanMurzak.McpPlugin.Chain.Testing;
using com.IvanMurzak.McpPlugin.NullEngine.Replay;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Chain
{
    /// <summary>
    /// The differential test that keeps the two implementations of the T2 canonical form in step:
    /// <c>scripts/chain_fixture.py</c> (run here as a subprocess) and
    /// <c>McpPlugin.NullEngine/src/Replay/FixtureLoader.cs</c>.
    ///
    /// <para>Two implementations exist because the recording tool must run in repositories with
    /// nothing installed (stdlib Python) while replay must run inside the plugin process (C#).
    /// Neither is generated from the other, so the ONLY thing keeping their bytes identical is
    /// this comparison — which is why the inputs below deliberately cover every rule the format
    /// has (F2 ordering, F3 masking, F4 caps) rather than a representative fixture.</para>
    ///
    /// <para>Precedent: <c>.pipeline/tests/_testlib.py</c> in the superproject, whose stdlib
    /// manifest parser is verified differentially against PyYAML the same way.</para>
    /// </summary>
    public sealed class CanonicalizeParityTests : IDisposable
    {
        readonly string _scratch = ChainToolchain.NewScratchDirectory();

        public void Dispose()
        {
            try
            {
                Directory.Delete(_scratch, recursive: true);
            }
            catch (Exception)
            {
                // Best effort.
            }
        }

        /// <summary>
        /// A fixture built to hit every canonicalisation rule at once: unordered keys, unordered
        /// lines, two argument sets under one name, an absolute Windows path, an absolute POSIX
        /// path, an ISO timestamp, a GUID, a URL with a port, a duration, key-driven pid/port
        /// masking, a JSON null INSIDE a payload (which must survive), non-ASCII including an
        /// astral-plane character, and number literals a re-formatting serializer would rewrite.
        /// </summary>
        static string MixedFixture() => string.Join("\n", new[]
        {
            "{\"kind\":\"meta\",\"schema\":1,\"engine\":\"mixed\",\"engine_version\":\"1.2.3\"," +
            "\"surface\":\"editor\",\"plugin_version\":\"0.1.0\",\"mcp_plugin_version\":\"8.3.0+abc\"," +
            "\"recorded_at\":\"2026-09-04T10:11:12Z\",\"recorder\":\"in-process\"}",

            "{\"kind\":\"tool\",\"name\":\"z/second\",\"description\":\"Sorted after a/first\"," +
            "\"inputSchema\":{\"type\":\"object\",\"properties\":{\"zeta\":{\"type\":\"string\"},\"alpha\":{\"type\":\"integer\"}}}," +
            "\"outputSchema\":null,\"openWorldHint\":false}",

            "{\"kind\":\"tool\",\"name\":\"a/first\",\"title\":\"First\",\"inputSchema\":{\"type\":\"object\"}," +
            "\"readOnlyHint\":true,\"idempotentHint\":false}",

            "{\"kind\":\"call\",\"name\":\"a/first\",\"args\":{\"b\":2,\"a\":1}," +
            "\"response\":{\"status\":\"success\",\"content\":[{\"type\":\"text\"," +
            "\"text\":\"win C:\\\\Users\\\\bob\\\\a.txt posix /home/bob/a.txt at 2026-09-04T10:11:12.5+02:00 " +
            "id 1b4e28ba-2fa1-11d2-883f-b9a761bde3fb via http://127.0.0.1:54321/mcp took 1234 ms\"}]," +
            "\"structuredContent\":{\"nothing\":null,\"pid\":4242,\"port\":8080,\"duration_ms\":17," +
            "\"slept_ms\":25,\"numbers\":[1,2.50,1e3,-0],\"text\":\"\\u4f60\\u597d \\u00e9 \\u2014 \\ud83d\\ude80\"}," +
            "\"errorKind\":\"None\"}}",

            "{\"kind\":\"call\",\"name\":\"a/first\",\"args\":{}," +
            "\"response\":{\"status\":\"error\",\"content\":[{\"type\":\"text\",\"text\":\"boom /variable is not a path\"}]," +
            "\"errorKind\":\"BadRequest\"}}",

            "{\"kind\":\"call\",\"name\":\"z/second\",\"args\":{\"nested\":{\"y\":1,\"x\":{\"deep\":true}}}," +
            "\"response\":{\"status\":\"success\",\"content\":[]," +
            "\"structuredContent\":{\"ok\":true}}}",
        }) + "\n";

        /// <summary>One response comfortably over F4's 32 KB cap, and one comfortably under it.</summary>
        static string CappedFixture() => string.Join("\n", new[]
        {
            "{\"kind\":\"meta\",\"schema\":1,\"engine\":\"capped\",\"engine_version\":\"0\"," +
            "\"surface\":\"runtime\",\"recorded_at\":\"2026-09-04T10:11:12Z\",\"recorder\":\"mcp-client\"}",
            "{\"kind\":\"tool\",\"name\":\"c/big\",\"inputSchema\":{\"type\":\"object\"}}",
            "{\"kind\":\"call\",\"name\":\"c/big\",\"args\":{\"size\":\"large\"}," +
            "\"response\":{\"status\":\"success\",\"content\":[{\"type\":\"text\",\"text\":\"" +
            new string('q', 40 * 1024) + "\"}]}}",
            "{\"kind\":\"call\",\"name\":\"c/big\",\"args\":{\"size\":\"small\"}," +
            "\"response\":{\"status\":\"success\",\"content\":[{\"type\":\"text\",\"text\":\"small\"}]}}",
        }) + "\n";

        public static IEnumerable<object[]> Fixtures()
        {
            yield return new object[] { "mixed", MixedFixture() };
            yield return new object[] { "capped", CappedFixture() };
        }

        [PythonTheory]
        [MemberData(nameof(Fixtures))]
        public void PythonAndCSharpProduceTheSameCanonicalBytes(string name, string source)
        {
            var input = Path.Combine(_scratch, name + ".jsonl");
            ChainToolchain.WriteText(input, source);

            foreach (var mode in new[]
            {
                new { Suffix = "plain", ForDiff = false, Cross = false, Args = Array.Empty<string>() },
                new { Suffix = "diff", ForDiff = true, Cross = false, Args = new[] { "--for-diff" } },
                new { Suffix = "cross", ForDiff = true, Cross = true, Args = new[] { "--for-diff", "--cross-recorder" } },
            })
            {
                var output = Path.Combine(_scratch, name + "." + mode.Suffix + ".jsonl");
                var arguments = new List<string> { "canonicalize", input };
                arguments.AddRange(mode.Args);
                arguments.AddRange(new[] { "--out", output });

                var result = ChainToolchain.RunScript(arguments.ToArray());
                result.ExitCode.ShouldBe(0, "chain_fixture.py " + string.Join(" ", arguments) + "\n" + result.Describe());

                var fromCSharp = FixtureLoader.Canonicalize(source, forDiff: mode.ForDiff, crossRecorder: mode.Cross, path: input);
                fromCSharp.ShouldBe(ChainToolchain.ReadText(output),
                    $"[{name}/{mode.Suffix}] the C# FixtureLoader and chain_fixture.py must agree byte for byte. " +
                    "A difference here is the two implementations of F2/F3/F4 drifting apart.");
            }
        }

        /// <summary>
        /// F3, both halves. Asserting only that the canonical form has no absolute path would pass
        /// on a canonicaliser that dropped the whole response - so the RAW input is checked to
        /// contain the paths first, in the same fixture family, before their absence means anything.
        /// </summary>
        [Fact]
        public void MaskingRemovesVolatileValues_AndTheRawInputProvesTheyWereThere()
        {
            var source = MixedFixture();

            source.Contains("C:\\\\Users\\\\bob", StringComparison.Ordinal).ShouldBeTrue(
                "the raw fixture must carry an absolute Windows path");
            source.Contains("/home/bob/a.txt", StringComparison.Ordinal).ShouldBeTrue(
                "the raw fixture must carry an absolute POSIX path");
            source.Contains("1b4e28ba-2fa1-11d2-883f-b9a761bde3fb", StringComparison.Ordinal).ShouldBeTrue(
                "the raw fixture must carry a GUID");
            source.Contains("2026-09-04T10:11:12.5+02:00", StringComparison.Ordinal).ShouldBeTrue(
                "the raw fixture must carry an ISO timestamp inside a response");

            var canonical = FixtureLoader.Canonicalize(source);

            canonical.Contains("Users\\\\bob", StringComparison.Ordinal).ShouldBeFalse("the Windows path must be masked");
            canonical.Contains("/home/bob", StringComparison.Ordinal).ShouldBeFalse("the POSIX path must be masked");
            canonical.Contains("1b4e28ba", StringComparison.Ordinal).ShouldBeFalse("the GUID must be masked");
            canonical.Contains("10:11:12.5+02:00", StringComparison.Ordinal).ShouldBeFalse("the timestamp must be masked");
            canonical.Contains(":54321", StringComparison.Ordinal).ShouldBeFalse("the port must be masked");

            canonical.Contains("<path>", StringComparison.Ordinal).ShouldBeTrue("masking must leave a TYPED placeholder");
            canonical.Contains("<guid>", StringComparison.Ordinal).ShouldBeTrue();
            canonical.Contains("<ts>", StringComparison.Ordinal).ShouldBeTrue();
            canonical.Contains("<port>", StringComparison.Ordinal).ShouldBeTrue();
            canonical.Contains("<ms>", StringComparison.Ordinal).ShouldBeTrue();
            canonical.Contains("<pid>", StringComparison.Ordinal).ShouldBeTrue("a numeric pid is reached by the KEY rule");

            // What masking must NOT do. Each of these is a value a sloppier rule would eat, and
            // eating it would silently remove the fixture's ability to discriminate on that field.
            canonical.Contains("\"slept_ms\":25", StringComparison.Ordinal).ShouldBeTrue(
                "slept_ms echoes an ARGUMENT rather than measuring anything, so it is not a duration");
            canonical.Contains("/variable is not a path", StringComparison.Ordinal).ShouldBeTrue(
                "'/variable' must not be read as the '/var' prefix plus junk");
            canonical.Contains("\"nothing\":null", StringComparison.Ordinal).ShouldBeTrue(
                "a null INSIDE a payload is content, not an absent field - null-engine/nested's " +
                "innermost child is exactly this shape");
            canonical.Contains("[1,2.50,1e3,-0]", StringComparison.Ordinal).ShouldBeTrue(
                "number tokens are copied verbatim; re-formatting them is what makes two " +
                "canonicalisers disagree");
            // Written as escapes, following NullEngineTools.UnicodeSample's reasoning: a raw glyph
            // in a C# literal depends on this file's encoding surviving every tool that rewrites it.
            canonical.Contains("你好 é — 🚀", StringComparison.Ordinal).ShouldBeTrue(
                "non-ASCII is written raw, astral-plane characters included");
        }

        /// <summary>
        /// The same guard <c>ChainToolchainAvailabilityTests</c> applies to the other assembly: a
        /// skipped parity test is invisible in a green run, so a machine that stopped resolving an
        /// interpreter would turn this whole class into a no-op. A plain <c>[Fact]</c> on purpose.
        /// </summary>
        [Fact]
        public void PythonGatedTests_ExistAndAreNotSkippedOnLinux()
        {
            var census = PythonFactCensus.Collect(typeof(CanonicalizeParityTests).Assembly);
            census.Count.ShouldBeGreaterThan(0,
                "at least one python-gated parity test must exist here, else the assertion below " +
                "is satisfied for free");

            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Linux))
                return;

            census.Where(entry => entry.Skip != null).Select(entry => entry.Name).ToList().ShouldBeEmpty(
                "hosted Linux ships python3, so a skip here means the parity check ran on nothing. " +
                "Resolved interpreter: " + (ChainToolchain.Python ?? "<none>"));
        }

        /// <summary>F4, both halves: the big response is hashed and the small one beside it is not.</summary>
        [Fact]
        public void OversizeResponsesAreHashed_AndSmallOnesAreNot()
        {
            var canonical = FixtureLoader.Canonicalize(CappedFixture());
            var lines = canonical.Split('\n').Where(line => line.Length > 0).ToList();

            var big = lines.Single(line => line.Contains("\"large\"", StringComparison.Ordinal));
            big.Contains("\"$hash\":\"sha256:", StringComparison.Ordinal).ShouldBeTrue("the 40 KB response must be hashed: " + Head(big));
            big.Contains("\"$bytes\":", StringComparison.Ordinal).ShouldBeTrue("a hashed response records the size it stood for");
            big.Contains("qqqq", StringComparison.Ordinal).ShouldBeFalse("the payload itself must be gone");

            var small = lines.Single(line => line.Contains("\"small\"", StringComparison.Ordinal));
            small.Contains("$hash", StringComparison.Ordinal).ShouldBeFalse("a small response is stored in full: " + Head(small));
            small.Contains("\"text\":\"small\"", StringComparison.Ordinal).ShouldBeTrue();
        }

        /// <summary>
        /// Canonicalising a canonical fixture is a no-op. Without this, `diff` comparing a
        /// COMMITTED file against a FRESH one would be comparing two different normal forms.
        /// </summary>
        [Fact]
        public void CanonicalizationIsIdempotent()
        {
            foreach (var source in new[] { MixedFixture(), CappedFixture() })
            {
                var once = FixtureLoader.Canonicalize(source);
                FixtureLoader.Canonicalize(once).ShouldBe(once);
                FixtureLoader.Canonicalize(once, forDiff: true).ShouldBe(FixtureLoader.Canonicalize(source, forDiff: true));
            }
        }

        /// <summary>
        /// args_hash parity. Replay looks a call up by this value, computed in C# from what the
        /// transport delivered; the fixture carries the value the Python recorder computed from
        /// what the client sent. If those two disagree, replay misses every call.
        /// </summary>
        [PythonTheory]
        [InlineData("{}")]
        [InlineData("{\"a\":1,\"b\":\"two\"}")]
        [InlineData("{\"b\":\"two\",\"a\":1}")]
        [InlineData("{\"payload\":{\"text\":\"round-trip\",\"number\":7,\"flag\":true}}")]
        [InlineData("{\"list\":[1,2,3],\"nothing\":null,\"deep\":{\"x\":{\"y\":-1}}}")]
        [InlineData("{\"unicode\":\"\\u4f60\\u597d \\ud83d\\ude80\"}")]
        public void ArgsHashAgreesBetweenPythonAndCSharp(string argumentsJson)
        {
            var result = ChainToolchain.RunScript("hash", "--args", argumentsJson);
            result.ExitCode.ShouldBe(0, result.Describe());

            var fromCSharp = FixtureLoader.ArgsHash(CanonicalJson.Parse(argumentsJson));
            fromCSharp.ShouldBe(result.StdOut.Trim(), "args_hash must agree for " + argumentsJson);
        }

        /// <summary>
        /// The transport's number-to-string conversion, pinned. A client sending <c>{"kb":1}</c>
        /// reaches the tool as <c>{"kb":"1"}</c> (measured against the real SignalR path), so the
        /// two must hash the same or replay can never match a call carrying a number. Written as a
        /// PAIR: the equality alone would also hold if ArgsHash returned a constant, which the
        /// inequality against a different value rules out.
        /// </summary>
        [Fact]
        public void ArgsHashIdentifiesANumberWithItsWireForm()
        {
            var asNumber = FixtureLoader.ArgsHash(CanonicalJson.Parse("{\"kb\":1,\"deep\":{\"n\":7}}"));
            var asWireStrings = FixtureLoader.ArgsHash(CanonicalJson.Parse("{\"kb\":\"1\",\"deep\":{\"n\":\"7\"}}"));
            asWireStrings.ShouldBe(asNumber,
                "the SignalR payload serializer turns numbers into strings on the way to the plugin, " +
                "so the two views of the same call must hash identically");

            var different = FixtureLoader.ArgsHash(CanonicalJson.Parse("{\"kb\":2,\"deep\":{\"n\":7}}"));
            different.ShouldNotBe(asNumber, "a DIFFERENT argument must still hash differently");

            // Booleans and strings are NOT rewritten by the transport, so they must not be folded.
            FixtureLoader.ArgsHash(CanonicalJson.Parse("{\"flag\":true}"))
                .ShouldNotBe(FixtureLoader.ArgsHash(CanonicalJson.Parse("{\"flag\":\"true\"}")),
                    "only numbers are identified with their text form");
        }

        /// <summary>
        /// The C# reader must refuse the same inputs the Python one refuses, with the same
        /// classification: a schema it does not understand and an over-cap fixture are exit-4
        /// conditions, not contract changes.
        /// </summary>
        [Fact]
        public void RefusalsMatchTheScriptsExitFourConditions()
        {
            var wrongSchema = "{\"schema\":2,\"kind\":\"meta\",\"engine\":\"x\"}\n";
            var schemaError = Should.Throw<FixtureFormatException>(() => FixtureLoader.Canonicalize(wrongSchema));
            schemaError.Message.ShouldContain("schema mismatch");

            var builder = new StringBuilder();
            builder.Append("{\"schema\":1,\"kind\":\"meta\",\"engine\":\"x\",\"engine_version\":\"0\"," +
                "\"surface\":\"editor\",\"recorder\":\"mcp-client\"}\n");
            var payload = new string('p', 30 * 1024);
            for (var i = 0; i < 12; i++)
            {
                builder.Append("{\"kind\":\"call\",\"name\":\"x/pad\",\"args\":{\"i\":").Append(i)
                    .Append("},\"response\":{\"status\":\"success\",\"content\":[{\"type\":\"text\",\"text\":\"")
                    .Append(payload).Append("\"}]}}\n");
            }
            var tooLarge = Should.Throw<FixtureFormatException>(() => FixtureLoader.Canonicalize(builder.ToString()));
            tooLarge.Message.ShouldContain("fixture too large: trim the battery");
        }

        static string Head(string line) => line.Length <= 200 ? line : line.Substring(0, 200) + "...";
    }
}
