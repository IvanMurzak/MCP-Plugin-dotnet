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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Chain.Testing
{
    /// <summary>
    /// Locates the T2 toolchain (<c>scripts/chain_fixture.py</c>, the committed fixtures) and runs
    /// the Python side of it.
    ///
    /// <para>Compiled into BOTH test projects — McpPlugin.Server.Tests owns the file, McpPlugin.Tests
    /// links it — because the parity test lives in one and the record/replay tests in the other, and
    /// two copies of "how do we find python?" would drift exactly when one of them starts skipping
    /// for a reason the other does not.</para>
    /// </summary>
    public static class ChainToolchain
    {
        /// <summary>The reason a python-gated test reports when the interpreter is missing.</summary>
        public const string NoPythonSkipReason = "python3 not on PATH";

        static readonly Lazy<string?> _python = new Lazy<string?>(ResolvePython);

        /// <summary>
        /// The interpreter to run <c>chain_fixture.py</c> with, or <see langword="null"/>.
        ///
        /// <para>Resolved by RUNNING each candidate rather than by looking for it on PATH: on
        /// Windows <c>python3</c> is frequently a Microsoft Store execution-alias stub that exists
        /// as a file, resolves on PATH, and does not run anything. A probe that only checked for
        /// the file would report "available" and every python-gated test would then fail instead of
        /// skipping.</para>
        /// </summary>
        public static string? Python => _python.Value;

        /// <summary>
        /// <c>&lt;repo&gt;/McpPlugin.*.Tests/bin/&lt;Configuration&gt;/&lt;tfm&gt;/</c> is where the
        /// test assembly runs, so the repository root is four directories up — the same derivation
        /// <c>NullEngineRealTransportTests.ResolveEngineDll</c> uses, and for the same reason: it
        /// adds no build-order coupling and copies nothing beside the test output.
        /// </summary>
        public static string RepositoryRoot
        {
            get
            {
                var testBin = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                return testBin.Parent?.Parent?.Parent?.Parent?.FullName ?? Directory.GetCurrentDirectory();
            }
        }

        public static string ScriptPath => Path.Combine(RepositoryRoot, "scripts", "chain_fixture.py");

        public static string FixtureDirectory => Path.Combine(RepositoryRoot, "tests", "chain-fixtures", "null-engine");

        public static string ReferenceFixture => Path.Combine(FixtureDirectory, "tools.jsonl");

        public static string Battery => Path.Combine(FixtureDirectory, "battery.json");

        public static string OversizeBattery => Path.Combine(FixtureDirectory, "battery-oversize.json");

        /// <summary>A fresh scratch directory that dies with the test.</summary>
        public static string NewScratchDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "chain-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        public sealed class ProcessResult
        {
            public int ExitCode { get; }
            public string StdOut { get; }
            public string StdErr { get; }

            public ProcessResult(int exitCode, string stdOut, string stdErr)
            {
                ExitCode = exitCode;
                StdOut = stdOut;
                StdErr = stdErr;
            }

            public string Describe() => $"exit={ExitCode}\nstdout:\n{StdOut}\nstderr:\n{StdErr}";
        }

        /// <summary>Runs <c>chain_fixture.py</c> and returns its exit code and captured output.</summary>
        public static ProcessResult RunScript(params string[] arguments)
        {
            var python = Python;
            if (python == null)
                throw new InvalidOperationException(NoPythonSkipReason);

            var startInfo = new ProcessStartInfo
            {
                FileName = python,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = RepositoryRoot,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            startInfo.ArgumentList.Add(ScriptPath);
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo)!;
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new ProcessResult(process.ExitCode, stdout, stderr);
        }

        /// <summary>Reads a fixture as raw UTF-8 text, so a byte comparison is a byte comparison.</summary>
        public static string ReadText(string path) => File.ReadAllText(path, Encoding.UTF8);

        /// <summary>Writes LF-separated UTF-8 without a BOM — the encoding F1 fixes.</summary>
        public static void WriteText(string path, string text)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory!);
            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        public static List<string> ReadFixtureLines(string path)
            => ReadText(path)
                .Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => line.Trim().Length > 0)
                .ToList();

        static string? ResolvePython()
        {
            foreach (var candidate in new[] { "python3", "python" })
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = candidate,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    };
                    startInfo.ArgumentList.Add("--version");

                    using var process = Process.Start(startInfo);
                    if (process == null)
                        continue;

                    var stdout = process.StandardOutput.ReadToEnd();
                    var stderr = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(20000))
                    {
                        // A Store alias stub can hang waiting for a UI. Do not leave it running.
                        try { process.Kill(entireProcessTree: true); } catch (Exception) { }
                        continue;
                    }

                    if (process.ExitCode != 0)
                        continue;

                    var banner = (stdout + stderr).Trim();
                    if (banner.StartsWith("Python 3.", StringComparison.Ordinal))
                        return candidate;
                }
                catch (Exception)
                {
                    // Not on PATH, or the OS refused to start it - try the next candidate.
                }
            }
            return null;
        }
    }

    /// <summary>
    /// A <see cref="FactAttribute"/> that SKIPS BY NAME when no Python 3 interpreter can be run,
    /// so a machine without one reports "python3 not on PATH" against the test rather than a
    /// failure — and the test still appears in the run, which is what
    /// <c>ChainToolchainAvailabilityTests</c> counts.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class PythonFactAttribute : FactAttribute
    {
        public PythonFactAttribute()
        {
            if (ChainToolchain.Python == null)
                Skip = ChainToolchain.NoPythonSkipReason;
        }
    }

    /// <summary>The <see cref="TheoryAttribute"/> counterpart of <see cref="PythonFactAttribute"/>.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class PythonTheoryAttribute : TheoryAttribute
    {
        public PythonTheoryAttribute()
        {
            if (ChainToolchain.Python == null)
                Skip = ChainToolchain.NoPythonSkipReason;
        }
    }

    /// <summary>Counts the python-gated tests in an assembly. See DoD 7's "the ubuntu leg asserts it ran".</summary>
    public static class PythonFactCensus
    {
        public static IReadOnlyList<(string Name, string? Skip)> Collect(Assembly assembly)
        {
            var found = new List<(string, string?)>();
            foreach (var type in assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    foreach (var attribute in method.GetCustomAttributes(inherit: true))
                    {
                        // Matched by TYPE rather than through a shared interface: FactAttribute and
                        // TheoryAttribute declare Skip independently, and a marker interface over
                        // both would only be a differently spelled version of this switch.
                        if (attribute is PythonFactAttribute fact)
                            found.Add((type.Name + "." + method.Name, fact.Skip));
                        else if (attribute is PythonTheoryAttribute theory)
                            found.Add((type.Name + "." + method.Name, theory.Skip));
                    }
                }
            }
            return found;
        }
    }
}
