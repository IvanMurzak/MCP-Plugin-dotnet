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
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;

namespace com.IvanMurzak.McpPlugin.NullEngine.Replay
{
    /// <summary>
    /// The IN-PROCESS recorder: it drives <c>IToolManager.RunListTool</c> and one
    /// <c>RunCallTool</c> per battery entry and writes an F1-shaped raw dump — the same lines a
    /// canonical fixture has, before sorting, masking, capping and <c>args_hash</c>, all of which
    /// <c>chain_fixture.py canonicalize</c> then applies.
    ///
    /// <para>This is the recorder an ENGINE leg runs inside its editor, where no MCP server is
    /// reachable. It needs no new McpPlugin API on purpose: an engine recorder that did could not
    /// compile in an ordinary PR run until MPD released and the engine repinned, which is the
    /// cross-repo lag this whole tier exists to remove.</para>
    ///
    /// <para><b>The projection is a MIRROR, and the parity test is its guard.</b> What a fixture
    /// records is what an MCP client observes, so this class reproduces the server's own mapping
    /// from a plugin response onto a <c>CallToolResult</c>:</para>
    /// <list type="bullet">
    ///   <item><description><c>ToolRouter.Call</c> (<c>McpPlugin.Server/src/Mcp/Tool/ToolRouter.Call.cs:86-95</c>)
    ///   — an error PACK discards the response's own content and rebuilds one text block from the
    ///   packed message; a null value becomes a fixed text.</description></item>
    ///   <item><description><c>ExtensionsTool.ToCallToolResult</c>
    ///   (<c>.../Extension/ExtensionsTool.cs:92-101</c>) — <c>IsError</c> from the status,
    ///   the content blocks mapped, the structured content serialized. <b>ErrorKind is not on that
    ///   list</b>, which is why an MCP-client capture cannot record it and this one can.</description></item>
    ///   <item><description><c>ExtensionsContentBlock.ToContent</c>
    ///   (<c>.../Extension/ExtensionsContentBlock.cs:18-49</c>) — note that a TEXT block loses its
    ///   MimeType on the wire, so this projection drops it too.</description></item>
    /// </list>
    /// <para>McpPlugin.Server cannot be referenced from here (the two pin different SignalR
    /// majors), so the mirror is a duplication by necessity. It is not left to drift: DoD 3's
    /// parity test records the SAME battery through a real server and through this class and
    /// compares the canonical bytes, which is exactly the assertion a drift breaks.</para>
    /// </summary>
    public static class RawDump
    {
        public sealed class BatteryCall
        {
            public string Name { get; }
            public CObject Args { get; }

            public BatteryCall(string name, CObject args)
            {
                Name = name;
                Args = args;
            }
        }

        public sealed class DumpResult
        {
            public int Tools { get; }
            public int Calls { get; }

            public DumpResult(int tools, int calls)
            {
                Tools = tools;
                Calls = calls;
            }
        }

        public static List<BatteryCall> LoadBattery(string path)
        {
            string text;
            try
            {
                text = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new FixtureFormatException($"cannot read battery {path}: {ex.Message}");
            }

            if (CanonicalJson.Parse(text) is not CObject document)
                throw new FixtureFormatException($"battery {path} must be a JSON object");

            var schema = document.Get("schema");
            var schemaText = schema is CNumber number ? number.Raw : schema is CString str ? str.Value : "<missing>";
            if (schemaText != FixtureLoader.Schema.ToString(CultureInfo.InvariantCulture))
                throw new FixtureFormatException($"battery {path}: schema mismatch - expected {FixtureLoader.Schema}, got {schemaText}");

            if (document.Get("calls") is not CArray calls || calls.Items.Count == 0)
                throw new FixtureFormatException($"battery {path} must carry a non-empty 'calls' array");

            var result = new List<BatteryCall>();
            foreach (var item in calls.Items)
            {
                if (item is not CObject entry || string.IsNullOrEmpty(entry.GetString("name")))
                    throw new FixtureFormatException($"battery {path}: every call needs a 'name'");
                var args = entry.Get("args") as CObject ?? new CObject();
                result.Add(new BatteryCall(entry.GetString("name")!, args));
            }
            return result;
        }

        public static async Task<DumpResult> WriteAsync(
            IToolManager toolManager,
            string outPath,
            string batteryPath,
            string engine,
            string engineVersion,
            string surface,
            string pluginVersion,
            string mcpPluginVersion,
            CancellationToken cancellationToken = default)
        {
            if (toolManager == null)
                throw new ArgumentNullException(nameof(toolManager));

            var battery = LoadBattery(batteryPath);
            var lines = new List<string>();

            var meta = new CObject();
            meta.Set("schema", new CNumber(FixtureLoader.Schema.ToString(CultureInfo.InvariantCulture)));
            meta.Set("kind", new CString("meta"));
            meta.Set("engine", new CString(engine));
            meta.Set("engine_version", new CString(engineVersion));
            meta.Set("surface", new CString(surface));
            meta.Set("plugin_version", new CString(pluginVersion));
            meta.Set("mcp_plugin_version", new CString(mcpPluginVersion));
            meta.Set("recorded_at", new CString(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
            meta.Set("recorder", new CString(FixtureLoader.RecorderInProcess));
            lines.Add(CanonicalJson.Write(meta));

            var listed = await toolManager.RunListTool(new RequestListTool(), cancellationToken).ConfigureAwait(false);
            if (listed.Status == ResponseStatus.Error || listed.Value == null)
                throw new FixtureFormatException("tools/list failed in-process: " + (listed.Message ?? "no message"));

            foreach (var tool in listed.Value)
                lines.Add(CanonicalJson.Write(ToolLine(tool)));

            foreach (var call in battery)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var arguments = ToArguments(call.Args);
                // IClientToolHub.RunCallTool takes no CancellationToken (only RunListTool does),
                // so the battery is cancellable between calls, not inside one.
                var outer = await toolManager
                    .RunCallTool(new RequestCallTool(call.Name, arguments))
                    .ConfigureAwait(false);

                var line = new CObject();
                line.Set("kind", new CString("call"));
                line.Set("name", new CString(call.Name));
                line.Set("args", call.Args);
                line.Set("response", ProjectResponse(outer));
                lines.Add(CanonicalJson.Write(line));
            }

            WriteAllLines(outPath, lines);
            return new DumpResult(listed.Value.Length, battery.Count);
        }

        /// <summary>Writes LF-separated UTF-8 without a BOM — the encoding F1 fixes.</summary>
        public static void WriteAllLines(string path, IEnumerable<string> lines)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory!);

            var builder = new StringBuilder();
            foreach (var line in lines)
                builder.Append(line).Append('\n');

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        internal static CObject ToolLine(ResponseListTool tool)
        {
            var line = new CObject();
            line.Set("kind", new CString("tool"));
            line.Set("name", new CString(tool.Name));

            if (!string.IsNullOrEmpty(tool.Title))
                line.Set("title", new CString(tool.Title!));
            if (!string.IsNullOrEmpty(tool.Description))
                line.Set("description", new CString(tool.Description!));
            if (tool.InputSchema.ValueKind != JsonValueKind.Undefined)
                line.Set("inputSchema", CanonicalJson.FromElement(tool.InputSchema));
            if (tool.OutputSchema.HasValue && tool.OutputSchema.Value.ValueKind != JsonValueKind.Undefined)
                line.Set("outputSchema", CanonicalJson.FromElement(tool.OutputSchema.Value));
            if (tool.ReadOnlyHint.HasValue)
                line.Set("readOnlyHint", new CBool(tool.ReadOnlyHint.Value));
            if (tool.DestructiveHint.HasValue)
                line.Set("destructiveHint", new CBool(tool.DestructiveHint.Value));
            if (tool.IdempotentHint.HasValue)
                line.Set("idempotentHint", new CBool(tool.IdempotentHint.Value));
            if (tool.OpenWorldHint.HasValue)
                line.Set("openWorldHint", new CBool(tool.OpenWorldHint.Value));

            // Deliberately NOT recorded: `Enabled` (tools/list serves only enabled tools to an
            // untrusted client, so an MCP capture could never see it) and the skill metadata
            // (behind the X-McpPlugin-Skill-Meta opt-in). Recording either would make the two
            // recorder kinds disagree for a reason that is not a contract change.
            return line;
        }

        internal static CObject ProjectResponse(ResponseData<ResponseCallTool> outer)
        {
            // ToolRouter.Call's own order: the packed error first, then the null value, then the
            // ordinary mapping. Getting this order wrong would record a success envelope for a
            // response the server turns into an error.
            if (outer.Status == ResponseStatus.Error)
                return ErrorEnvelope(outer.Message ?? "[Error] Got an error during running tool", outer.ErrorKind);

            if (outer.Value == null)
                return ErrorEnvelope("[Error] Tool returned null value", outer.ErrorKind);

            var value = outer.Value;
            var response = new CObject();
            response.Set("status", new CString(value.Status == ResponseStatus.Error ? "error" : "success"));

            var content = new CArray();
            if (value.Content != null)
            {
                foreach (var block in value.Content)
                    content.Items.Add(ToContent(block));
            }
            response.Set("content", content);

            if (value.StructuredContent != null)
                response.Set("structuredContent", CanonicalJson.FromNode(value.StructuredContent));

            response.Set("errorKind", new CString(outer.ErrorKind.ToString()));
            return response;
        }

        static CObject ErrorEnvelope(string message, ResponseErrorKind errorKind)
        {
            var block = new CObject();
            block.Set("type", new CString(Common.Consts.ContentType.Text));
            block.Set("text", new CString(message));

            var content = new CArray();
            content.Items.Add(block);

            var response = new CObject();
            response.Set("status", new CString("error"));
            response.Set("content", content);
            response.Set("errorKind", new CString(errorKind.ToString()));
            return response;
        }

        /// <summary>Mirror of <c>ExtensionsContentBlock.ToContent</c>, block kind by block kind.</summary>
        static CObject ToContent(ContentBlock block)
        {
            var output = new CObject();
            switch (block.Type)
            {
                case Common.Consts.ContentType.Image:
                case Common.Consts.ContentType.Audio:
                    output.Set("type", new CString(block.Type));
                    output.Set("data", new CString(block.Data ?? string.Empty));
                    output.Set("mimeType", new CString(block.MimeType ?? string.Empty));
                    return output;

                case Common.Consts.ContentType.Resource:
                    output.Set("type", new CString(Common.Consts.ContentType.Resource));
                    var resource = new CObject();
                    resource.Set("uri", new CString(block.Resource?.Uri ?? string.Empty));
                    if (block.Resource?.MimeType != null)
                        resource.Set("mimeType", new CString(block.Resource.MimeType));
                    // ToResourceContents picks Text first, then Blob - a resource carrying both
                    // reaches the wire as text only, so record it the same way.
                    if (block.Resource?.Text != null)
                        resource.Set("text", new CString(block.Resource.Text));
                    else if (block.Resource?.Blob != null)
                        resource.Set("blob", new CString(block.Resource.Blob));
                    output.Set("resource", resource);
                    return output;

                default:
                    // A text block LOSES its MimeType on the wire; recording it would make the two
                    // recorder kinds disagree on every error response.
                    output.Set("type", new CString(Common.Consts.ContentType.Text));
                    output.Set("text", new CString(block.Text ?? string.Empty));
                    return output;
            }
        }

        static IReadOnlyDictionary<string, JsonElement> ToArguments(CObject args)
        {
            var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var key in args.Keys)
            {
                using var document = JsonDocument.Parse(CanonicalJson.Write(args.Get(key)!));
                arguments[key] = document.RootElement.Clone();
            }
            return arguments;
        }
    }
}
