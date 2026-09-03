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
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.NullEngine.Tools;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.Logging;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.NullEngine
{
    /// <summary>
    /// The "null engine" host: a REAL headless McpPlugin process - real
    /// <see cref="McpPluginBuilder"/>, real SignalR transport, real attribute-scanned
    /// tool / prompt / resource registration - with no editor behind it.
    ///
    /// <para>It exists so that a test (or an out-of-repo consumer) has ONE deterministic,
    /// scriptable engine process to point an MCP server at. Every tool response is a pure
    /// function of its arguments, so two runs of the same call sequence are byte-comparable.</para>
    ///
    /// <para>The seed this replaces (<c>DemoConsoleApp</c>) connects but logs
    /// <c>fail: ... Cannot resolve relative SkillsPath 'SKILLS' ... ProjectRootPath is not set</c>
    /// because it never tells the plugin where the host project lives
    /// (<c>McpPlugin.ResolveSkillsPath</c>). This host always sets
    /// <see cref="ConnectionConfig.ProjectRootPath"/> - to <c>--project-root</c>, or to a fresh
    /// temp directory - which is why a clean run emits no <c>fail:</c> line at all. That
    /// assignment is load-bearing, not defensive: removing it brings the message straight back.
    /// Console logging deliberately stays at <see cref="LogLevel.Trace"/> so the
    /// <c>fail:</c> prefix remains observable.</para>
    ///
    /// <para>CLI contract, exit codes and the tool table live in <c>README.md</c> beside this file.</para>
    /// </summary>
    public static class Program
    {
        /// <summary>Prefix of the single line printed to stdout once the handshake succeeded.</summary>
        public const string ReadyLinePrefix = "NULL-ENGINE READY";

        /// <summary>Clean shutdown (Ctrl-C, <c>--exit-after-ms</c>, or <c>--help</c>).</summary>
        public const int ExitOk = 0;

        /// <summary>The version handshake did not complete inside the plugin timeout.</summary>
        public const int ExitHandshakeFailed = 3;

        public static async Task<int> Main(string[] args)
        {
            args ??= Array.Empty<string>();
            var parsed = ArgsUtils.ParseLineArguments(args);

            if (parsed.ContainsKey("help") || parsed.ContainsKey("h"))
            {
                Console.Out.Write(Usage());
                Console.Out.Flush();
                return ExitOk;
            }

            var projectRoot = ResolveProjectRoot(parsed);
            Directory.CreateDirectory(projectRoot);

            var readyFile = parsed.TryGetValue("ready-file", out var readyFileValue) && !string.IsNullOrWhiteSpace(readyFileValue)
                ? Path.GetFullPath(readyFileValue)
                : null;

            var exitAfterMs = ParseInt(parsed, "exit-after-ms", 0);
            var timeoutMs = ConnectionConfig.GetTimeoutFromArgsOrEnv(args);
            if (timeoutMs <= 0)
                timeoutMs = Consts.Hub.DefaultTimeoutMs;

            var endpoint = ConnectionConfig.GetEndpointFromArgsOrEnv(args);

            var version = new Version
            {
                Api = Consts.ApiVersion,
                Plugin = Consts.PluginVersion,
                Environment = "McpPlugin.NullEngine"
            };

            var reflector = new Reflector();

            var mcpPlugin = new McpPluginBuilder(version)
                .WithConfigFromArgsOrEnv(args)
                // A3: WithConfigFromArgsOrEnv sets only Host + TimeoutMs. Without ProjectRootPath the
                // relative default SkillsPath ('SKILLS') cannot be resolved and the very first skill
                // generation throws, which the plugin logs at Error level - the 'fail:' line this host
                // is required to never emit. Keep this chained call.
                .WithConfig(config => config.ProjectRootPath = projectRoot)
                .AddLogging(logging =>
                {
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Trace);
                })
                .WithToolsFromAssembly(typeof(NullEngineTools).Assembly)
                .WithPromptsFromAssembly(typeof(NullEngineTools).Assembly)
                .WithResourcesFromAssembly(typeof(NullEngineTools).Assembly)
                .Build(reflector);

            NullEngineHost.Logger = mcpPlugin.Logger;
            NullEngineHost.ProjectRoot = projectRoot;
            NullEngineHost.Endpoint = endpoint;

            using var lifetime = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                Cancel(lifetime);
            };

            var handshakeDeadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            using var handshakeCts = new CancellationTokenSource(timeoutMs);

            try
            {
                await mcpPlugin.Connect(handshakeCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Fall through to the deadline check below so the exit code has one source.
            }

            var ready = false;
            while (DateTime.UtcNow < handshakeDeadline)
            {
                var handshake = mcpPlugin.VersionHandshakeStatus;
                if (handshake != null && handshake.Compatible)
                {
                    ready = true;
                    break;
                }
                await Task.Delay(50).ConfigureAwait(false);
            }

            if (!ready)
            {
                Console.Error.WriteLine(
                    $"NULL-ENGINE HANDSHAKE-FAILED endpoint={endpoint} timeout-ms={timeoutMs}");
                Console.Error.Flush();
                mcpPlugin.Dispose();
                return ExitHandshakeFailed;
            }

            var toolNames = (mcpPlugin.McpManager.ToolManager?.GetAllTools() ?? Enumerable.Empty<IRunTool>())
                .Select(tool => tool.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var promptCount = mcpPlugin.McpManager.PromptManager?.GetAllPrompts().Count() ?? 0;
            var resourceCount = mcpPlugin.McpManager.ResourceManager?.GetAllResources().Count() ?? 0;
            var pluginVersion = ResolvePluginInformationalVersion();

            NullEngineHost.SetToolNames(toolNames);
            NullEngineHost.SetCounts(toolNames.Length, promptCount, resourceCount, pluginVersion);

            Console.Out.WriteLine(
                $"{ReadyLinePrefix} tools={toolNames.Length} prompts={promptCount} resources={resourceCount} plugin={pluginVersion}");
            Console.Out.Flush();

            if (readyFile != null)
                WriteReadyFile(readyFile, toolNames.Length, promptCount, resourceCount, pluginVersion);

            if (exitAfterMs > 0)
                lifetime.CancelAfter(exitAfterMs);

            try
            {
                await Task.Delay(Timeout.Infinite, lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Requested shutdown - the only way out of the idle wait.
            }

            mcpPlugin.Dispose();
            return ExitOk;
        }

        /// <summary>
        /// Writes the ready file through a temp file + rename so a reader can never observe a
        /// half-written document: the test waits on this file's EXISTENCE, so a partial write
        /// would surface as an opaque JSON parse error rather than as a slow start.
        /// </summary>
        static void WriteReadyFile(string readyFile, int tools, int prompts, int resources, string pluginVersion)
        {
            var directory = Path.GetDirectoryName(readyFile);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory!);

            var payload = new JsonObject
            {
                ["pid"] = Environment.ProcessId,
                ["tools"] = tools,
                ["prompts"] = prompts,
                ["resources"] = resources,
                ["plugin_version"] = pluginVersion
            };

            var temp = readyFile + ".tmp";
            File.WriteAllText(temp, payload.ToJsonString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(readyFile))
                File.Delete(readyFile);
            File.Move(temp, readyFile);
        }

        static void Cancel(CancellationTokenSource source)
        {
            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already shutting down.
            }
        }

        static string ResolveProjectRoot(IDictionary<string, string> parsed)
        {
            if (parsed.TryGetValue("project-root", out var value) && !string.IsNullOrWhiteSpace(value))
                return Path.GetFullPath(value);

            return Path.Combine(Path.GetTempPath(), "mcp-null-engine-" + Guid.NewGuid().ToString("N"));
        }

        static int ParseInt(IDictionary<string, string> parsed, string key, int fallback)
            => parsed.TryGetValue(key, out var value) && int.TryParse(value, out var parsedValue)
                ? parsedValue
                : fallback;

        /// <summary>
        /// The McpPlugin ASSEMBLY's informational version, read at runtime - NOT
        /// <c>Consts.PluginVersion</c>, a compile-time constant that cannot tell a locally built
        /// assembly from a released one, and therefore cannot discriminate at all.
        /// </summary>
        static string ResolvePluginInformationalVersion()
        {
            var assembly = typeof(global::com.IvanMurzak.McpPlugin.McpPlugin).Assembly;
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (!string.IsNullOrEmpty(informational))
                return informational!;

            return assembly.GetName().Version?.ToString() ?? "unknown";
        }

        static string Usage()
        {
            var builder = new StringBuilder();
            builder.AppendLine("McpPlugin.NullEngine - a real headless McpPlugin host (no editor).");
            builder.AppendLine();
            builder.AppendLine("Usage:");
            builder.AppendLine("  dotnet McpPlugin.NullEngine.dll --mcp-server-endpoint=<url> [options]");
            builder.AppendLine();
            builder.AppendLine("Options:");
            builder.AppendLine("  --mcp-server-endpoint=<url>  MCP server base URL. Env: MCP_SERVER_ENDPOINT.");
            builder.AppendLine($"                               Default: {Consts.Hub.DefaultHost}");
            builder.AppendLine("  --project-root=<dir>         Host project root; anchors the relative SkillsPath.");
            builder.AppendLine("                               Default: a fresh temp directory.");
            builder.AppendLine("  --ready-file=<path>          Write {pid, tools, prompts, resources, plugin_version}");
            builder.AppendLine("                               as JSON once the handshake succeeded.");
            builder.AppendLine("  --exit-after-ms=<n>          Exit 0 after n milliseconds of being ready.");
            builder.AppendLine("  --mcp-server-timeout=<ms>    Handshake timeout. Env: MCP_SERVER_TIMEOUT.");
            builder.AppendLine($"                               Default: {Consts.Hub.DefaultTimeoutMs}");
            builder.AppendLine("  --help                       Print this text and exit 0 without connecting.");
            builder.AppendLine();
            builder.AppendLine("Ready line (stdout, exactly once):");
            builder.AppendLine($"  {ReadyLinePrefix} tools=<n> prompts=<n> resources=<n> plugin=<informational version>");
            builder.AppendLine();
            builder.AppendLine("Exit codes:");
            builder.AppendLine($"  {ExitOk}  clean shutdown (Ctrl-C, --exit-after-ms, --help)");
            builder.AppendLine($"  {ExitHandshakeFailed}  the version handshake did not complete inside the timeout");
            return builder.ToString();
        }
    }
}
