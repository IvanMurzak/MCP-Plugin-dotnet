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
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using com.IvanMurzak.McpPlugin.Chain.Testing;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests.Chain
{
    /// <summary>
    /// The guard that keeps the python-gated T2 tests from silently disappearing.
    ///
    /// <para>A skip is invisible in a green run, so a machine that quietly stopped resolving an
    /// interpreter would turn every assertion in <c>ChainRecordReplayTests</c> and
    /// <c>CanonicalizeParityTests</c> into a no-op while CI stayed green. This class fails instead
    /// — and it is a plain <c>[Fact]</c> precisely because a <c>[PythonFact]</c> would skip for the
    /// very condition it exists to detect.</para>
    ///
    /// <para>The workflow file is not touched by this task, so "the ubuntu leg asserts the python
    /// tests ran" is expressed HERE rather than as a CI step: hosted Linux always ships
    /// <c>python3</c>, so requiring it there is a real gate, while the other legs still get the
    /// count assertion below.</para>
    /// </summary>
    public sealed class ChainToolchainAvailabilityTests
    {
        [Fact]
        public void PythonGatedTests_ExistAndAreNotSkippedOnLinux()
        {
            var census = PythonFactCensus.Collect(typeof(ChainRecordReplayTests).Assembly);

            census.Count.ShouldBeGreaterThan(0,
                "at least one python-gated T2 test must exist in this assembly - if the census is " +
                "empty, the 'they were not skipped' assertion below is satisfied for free");

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            var skipped = census.Where(entry => entry.Skip != null).Select(entry => entry.Name).ToList();
            skipped.ShouldBeEmpty(
                "hosted Linux ships python3, so a skip here means the interpreter probe stopped " +
                "working and the T2 suite is running on nothing. Resolved interpreter: " +
                (ChainToolchain.Python ?? "<none>"));
        }

        [Fact]
        public void TheToolchainIsWhereTheTestsLookForIt()
        {
            // Every python-gated test derives these paths from AppContext.BaseDirectory. If the
            // derivation breaks (a changed output layout, a different Configuration nesting), the
            // script simply would not be found - and the failure would read as "python is broken"
            // rather than "the path derivation is wrong". Say which it is, once, here.
            File.Exists(ChainToolchain.ScriptPath).ShouldBeTrue(
                "scripts/chain_fixture.py must be reachable from the test output directory; " +
                "derived repository root: " + ChainToolchain.RepositoryRoot);
            File.Exists(ChainToolchain.Battery).ShouldBeTrue(
                "the reference battery must be committed at " + ChainToolchain.Battery);
            File.Exists(ChainToolchain.OversizeBattery).ShouldBeTrue(
                "the oversize battery must be committed at " + ChainToolchain.OversizeBattery);
            File.Exists(ChainToolchain.ReferenceFixture).ShouldBeTrue(
                "the reference fixture must be committed at " + ChainToolchain.ReferenceFixture);
        }
    }
}
