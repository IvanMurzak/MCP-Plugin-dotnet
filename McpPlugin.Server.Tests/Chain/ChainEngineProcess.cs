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
using System.Text;
using System.Threading;
using Shouldly;

namespace com.IvanMurzak.McpPlugin.Chain.Testing
{
    /// <summary>
    /// A running <c>dotnet McpPlugin.NullEngine.dll …</c> subprocess for the T2 tests, with
    /// line-buffered output capture.
    ///
    /// <para>Separate from the private harness inside <c>NullEngineRealTransportTests</c> on
    /// purpose: that one owns the T1 CLI contract (ready file, exit 3, fail-block accounting) and
    /// is deliberately closed over its own literals, while this one has to pass arbitrary extra
    /// flags (<c>--replay</c>, <c>--dump-raw</c>, <c>--battery</c>) and to run the host WITHOUT a
    /// server at all. Widening the T1 harness to do both would couple the two contracts.</para>
    /// </summary>
    public sealed class ChainEngineProcess : IDisposable
    {
        public const string ReadyLinePrefix = "NULL-ENGINE READY";
        public const string ReplayLinePrefix = "NULL-ENGINE REPLAY";
        public const string DumpRawLinePrefix = "NULL-ENGINE DUMP-RAW";

        readonly Process _process;
        readonly List<string> _stdout = new();
        readonly List<string> _stderr = new();
        readonly object _gate = new();
        readonly string _workDirectory;

        ChainEngineProcess(ProcessStartInfo startInfo, string workDirectory)
        {
            _workDirectory = workDirectory;
            _process = new Process { StartInfo = startInfo };
            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (_gate) _stdout.Add(e.Data);
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (_gate) _stderr.Add(e.Data);
            };
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        /// <summary>Starts the engine connected to <paramref name="endpoint"/>, plus any extra flags.</summary>
        public static ChainEngineProcess StartConnected(string endpoint, params string[] extraArguments)
            => Start(new[]
            {
                "--mcp-server-endpoint=" + endpoint,
                "--mcp-server-timeout=30000",
            }.Concat(extraArguments).ToArray());

        /// <summary>Starts the engine with exactly these flags (no endpoint is added).</summary>
        public static ChainEngineProcess Start(params string[] arguments)
        {
            var engineDll = ResolveEngineDll();
            File.Exists(engineDll).ShouldBeTrue(
                "the null engine was not found at " + engineDll +
                " - McpPlugin.NullEngine must be part of McpPlugin.sln so a solution build produces it, " +
                "or set MCPPLUGIN_NULLENGINE_DLL to an explicit path");

            var workDirectory = Path.Combine(Path.GetTempPath(), "chain-engine-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDirectory);

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = AppContext.BaseDirectory,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            // Same reasoning as the T1 harness: ConnectionConfig reads these from the environment,
            // so an ambient value could change the run for a purely environmental reason.
            startInfo.Environment.Remove("MCP_SKILLS_FOLDER");
            startInfo.Environment.Remove("MCP_SERVER_ENDPOINT");
            startInfo.Environment.Remove("MCP_SERVER_TIMEOUT");

            startInfo.ArgumentList.Add(engineDll);
            startInfo.ArgumentList.Add("--project-root=" + Path.Combine(workDirectory, "project"));
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            return new ChainEngineProcess(startInfo, workDirectory);
        }

        static string ResolveEngineDll()
        {
            var explicitPath = Environment.GetEnvironmentVariable("MCPPLUGIN_NULLENGINE_DLL");
            if (!string.IsNullOrWhiteSpace(explicitPath))
                return Path.GetFullPath(explicitPath!);

            var testBin = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var targetFramework = testBin.Name;
            var configuration = testBin.Parent?.Name ?? "Release";

            return Path.Combine(ChainToolchain.RepositoryRoot, "McpPlugin.NullEngine", "bin",
                configuration, targetFramework, "McpPlugin.NullEngine.dll");
        }

        public bool HasExited => _process.HasExited;
        public int ExitCode => _process.ExitCode;

        public IReadOnlyList<string> StdoutLines
        {
            get { lock (_gate) return _stdout.ToArray(); }
        }

        public IReadOnlyList<string> StderrLines
        {
            get { lock (_gate) return _stderr.ToArray(); }
        }

        /// <summary>
        /// Waits for a captured stdout line starting with <paramref name="prefix"/>. Returns null
        /// on timeout OR once the process has exited without producing one — an exited process can
        /// never produce it, so waiting the full timeout would only slow a failure down.
        /// </summary>
        public string? WaitForStdoutLine(string prefix, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                var hit = StdoutLines.FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
                if (hit != null)
                    return hit;
                if (_process.HasExited)
                {
                    // One last read: the capture callbacks are asynchronous, so a line written
                    // microseconds before exit may not be in the list yet.
                    _process.WaitForExit();
                    return StdoutLines.FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
                }
                if (DateTime.UtcNow >= deadline)
                    return null;
                Thread.Sleep(25);
            }
        }

        public string WaitForReady(TimeSpan timeout)
        {
            var line = WaitForStdoutLine(ReadyLinePrefix, timeout);
            line.ShouldNotBeNull("the null engine never became ready within " + timeout + ". " + Describe());
            return line!;
        }

        /// <summary>Tool count parsed out of the ready line's <c>tools=&lt;n&gt;</c> field.</summary>
        public static int ToolsFromReadyLine(string readyLine)
        {
            var token = readyLine.Split(' ').FirstOrDefault(part => part.StartsWith("tools=", StringComparison.Ordinal));
            token.ShouldNotBeNull("the ready line must carry a tools=<n> field: " + readyLine);
            return int.Parse(token!.Substring("tools=".Length), CultureInfo.InvariantCulture);
        }

        public bool ExitedWithin(TimeSpan window)
        {
            if (!_process.WaitForExit((int)window.TotalMilliseconds))
                return false;
            // The timed overload returns before the asynchronous output handlers drain.
            _process.WaitForExit();
            return true;
        }

        public void Kill()
        {
            if (_process.HasExited)
                return;
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(15000);
        }

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
            // Both catches are broad for the same reason as the T1 harness: an exception thrown out
            // of Dispose REPLACES the assertion failure that is unwinding through it.
            try
            {
                Kill();
            }
            catch (Exception)
            {
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
                }
            }
        }
    }
}
