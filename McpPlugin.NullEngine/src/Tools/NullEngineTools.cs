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
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.NullEngine.Tools
{
    /// <summary>
    /// The representative tool surface of the null engine. Every response is a pure function of the
    /// call's arguments - no clock, no randomness, no ambient state - so the same call sequence
    /// replayed later produces byte-identical results, which is what makes this host usable as a
    /// diff fixture.
    ///
    /// <para>Two of these tools are DELIBERATELY failing (<c>fail</c>, <c>throw</c>). They exist so
    /// a consumer can prove it distinguishes a tool-level error from a transport error, and so this
    /// repo can prove that an exception inside a tool does NOT take the host process down.</para>
    /// </summary>
    [AiToolType]
    public static class NullEngineTools
    {
        /// <summary>Every tool name is prefixed with this, so a consumer can filter the catalog.</summary>
        public const string Prefix = "null-engine/";

        /// <summary>The exact text <c>null-engine/fail</c> returns. Fixed so a consumer can assert on it.</summary>
        public const string FailMessage = "null-engine: deliberate tool failure (fixed message).";

        /// <summary>The exact text <c>null-engine/throw</c> throws. Fixed so a consumer can assert on it.</summary>
        public const string ThrowMessage = "null-engine: deliberate unhandled exception (fixed message).";

        /// <summary>
        /// Default payload of <c>null-engine/unicode</c> when no text is supplied: CJK, Latin-1
        /// accents, an em dash and an astral-plane emoji (a surrogate pair). Written as escapes on
        /// purpose - a raw glyph in a C# literal depends on the file's encoding surviving every tool
        /// that ever rewrites this file, and the escaped form is byte-identical everywhere.
        /// </summary>
        public const string UnicodeSample = "\u4F60\u597D \u00E9\u00E8\u00EA \u00DF \u2014 \uD83D\uDE80";

        /// <summary>Upper bound on <c>null-engine/large-payload</c>, so a typo cannot allocate a gigabyte.</summary>
        public const int MaxPayloadKb = 1024;

        /// <summary>Upper bound on <c>null-engine/slow</c>, so a typo cannot park the host for an hour.</summary>
        public const int MaxSleepMs = 10000;

        /// <summary>Upper bound on <c>null-engine/progress</c>.</summary>
        public const int MaxProgressSteps = 100;

        /// <summary>Serialization used for every structured response this class builds itself.</summary>
        static readonly JsonSerializerOptions _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            WriteIndented = false
        };

        [AiTool(Prefix + "ping", "Ping")]
        [Description("Takes no arguments and always answers 'pong'. The liveness probe.")]
        public static string Ping() => "pong";

        [AiTool(Prefix + "echo", "Echo")]
        [Description("Returns the supplied payload unchanged - the structured round-trip probe.")]
        public static ResponseCallTool Echo(
            [Description("The object to return unchanged.")] EchoPayload? payload)
            => ResponseCallTool.SuccessStructured(
                JsonSerializer.SerializeToNode(payload ?? new EchoPayload(), _json));

        [AiTool(Prefix + "large-payload", "Large payload")]
        [Description("Returns exactly the requested number of kibibytes of the character 'x'.")]
        public static ResponseCallTool LargePayload(
            [Description("How many kibibytes to return (0..1024).")] int kb = 1)
        {
            if (kb < 0 || kb > MaxPayloadKb)
                return ResponseCallTool.Error($"kb must be between 0 and {MaxPayloadKb}, got {kb}.", ResponseErrorKind.BadRequest);

            return ResponseCallTool.SuccessStructured(new JsonObject
            {
                ["kb"] = kb,
                ["length"] = kb * 1024,
                ["payload"] = new string('x', kb * 1024)
            });
        }

        [AiTool(Prefix + "fail", "Fail")]
        [Description("Always returns a tool-level error with a fixed message. Never throws.")]
        public static ResponseCallTool Fail()
            => ResponseCallTool.Error(FailMessage, ResponseErrorKind.Internal);

        [AiTool(Prefix + "throw", "Throw")]
        [Description("Always throws. The host must surface an error and STAY ALIVE.")]
        public static string Throw()
            => throw new InvalidOperationException(ThrowMessage);

        [AiTool(Prefix + "slow", "Slow")]
        [Description("Awaits the requested number of milliseconds, then answers 'slept'.")]
        public static async Task<ResponseCallTool> Slow(
            [Description("How long to await, in milliseconds (0..10000).")] int ms = 0)
        {
            if (ms < 0 || ms > MaxSleepMs)
                return ResponseCallTool.Error($"ms must be between 0 and {MaxSleepMs}, got {ms}.", ResponseErrorKind.BadRequest);

            await Task.Delay(ms).ConfigureAwait(false);

            return ResponseCallTool.SuccessStructured(new JsonObject
            {
                ["slept_ms"] = ms,
                ["status"] = "slept"
            });
        }

        [AiTool(Prefix + "progress", "Progress")]
        [Description("Emits the requested number of progress log lines, then reports how many it emitted.")]
        public static ResponseCallTool Progress(
            [Description("How many progress notices to emit (0..100).")] int steps = 3)
        {
            if (steps < 0 || steps > MaxProgressSteps)
                return ResponseCallTool.Error($"steps must be between 0 and {MaxProgressSteps}, got {steps}.", ResponseErrorKind.BadRequest);

            // The plugin has no progress-notification API on the tool seam today, so the fallback the
            // contract allows is taken: one log line per step, through the plugin's own logger rather
            // than Console (which would interleave with the ready line on stdout).
            for (var step = 1; step <= steps; step++)
                NullEngineHost.Logger?.LogInformation("null-engine progress {step}/{steps}", step, steps);

            return ResponseCallTool.SuccessStructured(new JsonObject
            {
                ["emitted"] = steps
            });
        }

        [AiTool(Prefix + "unicode", "Unicode")]
        [Description("Returns the supplied text unchanged - the non-ASCII round-trip probe.")]
        public static ResponseCallTool Unicode(
            [Description("Text to echo back. Defaults to a mixed non-ASCII sample.")] string? text = null)
            => ResponseCallTool.SuccessStructured(new JsonObject
            {
                ["text"] = string.IsNullOrEmpty(text) ? UnicodeSample : text
            });

        [AiTool(Prefix + "nested", "Nested")]
        [Description("Returns a five-level-deep JSON document.")]
        public static ResponseCallTool Nested()
        {
            JsonNode? child = null;
            for (var level = 5; level >= 1; level--)
            {
                child = new JsonObject
                {
                    ["level"] = level,
                    ["label"] = "level-" + level.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["child"] = child
                };
            }

            return ResponseCallTool.SuccessStructured(child);
        }

        [AiTool(Prefix + "enum-arg", "Enum argument")]
        [Description("Accepts one of red / green / blue and rejects anything else.")]
        public static ResponseCallTool EnumArg(
            [Description("One of: Red, Green, Blue.")] NullEngineColor color = NullEngineColor.Red)
        {
            // Explicit rather than relying on the binder: an undefined value can still reach an enum
            // parameter (Enum.ToObject accepts any integer), so the rejection this tool advertises has
            // to be its own check to be a rejection at all.
            if (!Enum.IsDefined(typeof(NullEngineColor), color))
                return ResponseCallTool.Error($"color must be one of Red, Green, Blue; got '{color}'.", ResponseErrorKind.BadRequest);

            return ResponseCallTool.SuccessStructured(new JsonObject
            {
                ["color"] = color.ToString()
            });
        }

        [AiTool(Prefix + "optional-args", "Optional arguments")]
        [Description("Reports the values it was invoked with, so omitted arguments show their defaults.")]
        public static ResponseCallTool OptionalArgs(
            [Description("Any name. Defaults to 'world'.")] string name = "world",
            [Description("Any count. Defaults to 1.")] int count = 1)
            => ResponseCallTool.SuccessStructured(new JsonObject
            {
                ["name"] = name,
                ["count"] = count
            });

        [AiTool(Prefix + "list-self", "List self")]
        [Description("Returns the tool names this plugin actually registered, ordered ordinally.")]
        public static ResponseCallTool ListSelf()
        {
            var names = new JsonArray();
            foreach (var name in NullEngineHost.ToolNames)
                names.Add(name);

            return ResponseCallTool.SuccessStructured(new JsonObject
            {
                ["tools"] = names
            });
        }
    }

    /// <summary>The structured argument (and result) of <c>null-engine/echo</c>.</summary>
    public sealed class EchoPayload
    {
        public string Text { get; set; } = string.Empty;
        public int Number { get; set; }
        public bool Flag { get; set; }
    }

    /// <summary>The validated enum of <c>null-engine/enum-arg</c>.</summary>
    public enum NullEngineColor
    {
        Red,
        Green,
        Blue
    }
}
