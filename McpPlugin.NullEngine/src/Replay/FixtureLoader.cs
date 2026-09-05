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
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace com.IvanMurzak.McpPlugin.NullEngine.Replay
{
    /// <summary>
    /// A fixture that cannot be read as <see cref="FixtureLoader.Schema"/>. Every path that raises
    /// this is an exit-4 condition, never a contract verdict: the two documents are not comparable.
    /// </summary>
    public sealed class FixtureFormatException : Exception
    {
        public FixtureFormatException(string message) : base(message) { }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // Canonical JSON value model
    // ═══════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A JSON value in the form F2 canonicalisation needs.
    ///
    /// <para><b>Why not <c>JsonNode</c>.</b> Canonicalisation has to (a) preserve a number's
    /// ORIGINAL token text and (b) control every escape byte a string produces. <c>JsonNode</c>
    /// re-formats numbers through the runtime's shortest-round-trip rules and escapes strings
    /// through a <see cref="JavaScriptEncoder"/> whose output differs from Python's
    /// (<c>"</c> vs <c>\"</c>, and non-ASCII escaping by default) — so the two
    /// implementations of this format would disagree byte-for-byte on inputs that are perfectly
    /// ordinary. This model plus <see cref="CanonicalJson"/> makes both decisions explicit.</para>
    /// </summary>
    public abstract class CNode
    {
    }

    public sealed class CNull : CNode
    {
        public static readonly CNull Instance = new CNull();
        CNull() { }
    }

    public sealed class CBool : CNode
    {
        public bool Value { get; }
        public CBool(bool value) => Value = value;
    }

    /// <summary>A JSON number kept as the exact token text it was parsed from.</summary>
    public sealed class CNumber : CNode
    {
        public string Raw { get; }
        public CNumber(string raw) => Raw = raw;
    }

    public sealed class CString : CNode
    {
        public string Value { get; }
        public CString(string value) => Value = value;
    }

    public sealed class CArray : CNode
    {
        public List<CNode> Items { get; } = new List<CNode>();
    }

    /// <summary>
    /// A JSON object preserving insertion order for construction and de-duplicating keys the way
    /// a stdlib JSON parser does (last one wins), because <c>JsonDocument</c> keeps duplicates and
    /// Python's <c>json</c> does not — a divergence that would show up only on a malformed input.
    /// </summary>
    public sealed class CObject : CNode
    {
        readonly List<string> _order = new List<string>();
        readonly Dictionary<string, CNode> _members = new Dictionary<string, CNode>(StringComparer.Ordinal);

        public int Count => _order.Count;
        public IEnumerable<string> Keys => _order;

        public bool TryGet(string key, out CNode value) => _members.TryGetValue(key, out value!);

        public CNode? Get(string key) => _members.TryGetValue(key, out var value) ? value : null;

        public bool Has(string key) => _members.ContainsKey(key);

        public void Set(string key, CNode value)
        {
            if (!_members.ContainsKey(key))
                _order.Add(key);
            _members[key] = value;
        }

        public void Remove(string key)
        {
            if (_members.Remove(key))
                _order.Remove(key);
        }

        public string? GetString(string key) => Get(key) is CString text ? text.Value : null;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // F2 canonical JSON
    // ═══════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The C# half of the F2 canonical form. <c>scripts/chain_fixture.py</c> is the other half;
    /// <c>McpPlugin.Tests/Chain/CanonicalizeParityTests.cs</c> runs both over the same input and
    /// compares bytes, so a change here that is not mirrored there reddens.
    /// </summary>
    public static class CanonicalJson
    {
        /// <summary>Orders object keys and fixture lines by their UTF-8 BYTES.</summary>
        public sealed class Utf8Comparer : IComparer<string>
        {
            public static readonly Utf8Comparer Instance = new Utf8Comparer();

            public int Compare(string? x, string? y)
            {
                var left = Encoding.UTF8.GetBytes(x ?? string.Empty);
                var right = Encoding.UTF8.GetBytes(y ?? string.Empty);
                var shared = Math.Min(left.Length, right.Length);
                for (var i = 0; i < shared; i++)
                {
                    if (left[i] != right[i])
                        return left[i].CompareTo(right[i]);
                }
                return left.Length.CompareTo(right.Length);
            }
        }

        public static CNode Parse(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                return FromElement(document.RootElement);
            }
            catch (JsonException ex)
            {
                throw new FixtureFormatException("not valid JSON: " + ex.Message);
            }
        }

        public static CNode FromElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var obj = new CObject();
                    foreach (var property in element.EnumerateObject())
                        obj.Set(property.Name, FromElement(property.Value));
                    return obj;

                case JsonValueKind.Array:
                    var array = new CArray();
                    foreach (var item in element.EnumerateArray())
                        array.Items.Add(FromElement(item));
                    return array;

                case JsonValueKind.String:
                    return new CString(element.GetString() ?? string.Empty);

                case JsonValueKind.Number:
                    // The RAW text, not a parsed double — see CNode's remarks.
                    return new CNumber(element.GetRawText());

                case JsonValueKind.True:
                    return new CBool(true);

                case JsonValueKind.False:
                    return new CBool(false);

                default:
                    return CNull.Instance;
            }
        }

        public static CNode FromNode(JsonNode? node)
        {
            if (node == null)
                return CNull.Instance;
            return Parse(node.ToJsonString());
        }

        public static string Write(CNode node)
        {
            var builder = new StringBuilder();
            Write(node, builder);
            return builder.ToString();
        }

        public static void Write(CNode node, StringBuilder builder)
        {
            switch (node)
            {
                case CNull:
                    builder.Append("null");
                    return;
                case CBool boolean:
                    builder.Append(boolean.Value ? "true" : "false");
                    return;
                case CNumber number:
                    builder.Append(number.Raw);
                    return;
                case CString text:
                    WriteString(text.Value, builder);
                    return;
                case CArray array:
                    builder.Append('[');
                    for (var i = 0; i < array.Items.Count; i++)
                    {
                        if (i > 0)
                            builder.Append(',');
                        Write(array.Items[i], builder);
                    }
                    builder.Append(']');
                    return;
                case CObject obj:
                    builder.Append('{');
                    var keys = obj.Keys.ToList();
                    keys.Sort(Utf8Comparer.Instance);
                    for (var i = 0; i < keys.Count; i++)
                    {
                        if (i > 0)
                            builder.Append(',');
                        WriteString(keys[i], builder);
                        builder.Append(':');
                        Write(obj.Get(keys[i])!, builder);
                    }
                    builder.Append('}');
                    return;
                default:
                    throw new FixtureFormatException("unsupported JSON value: " + node.GetType().Name);
            }
        }

        /// <summary>
        /// The escape table, spelled out rather than delegated to <c>System.Text.Json</c>.
        /// Only <c>"</c>, <c>\</c>, the five named control escapes and the remaining C0 range are
        /// escaped; everything else — including all non-ASCII — is written raw, which is what makes
        /// the UTF-8 bytes identical to Python's <c>ensure_ascii=False</c> output.
        /// </summary>
        public static void WriteString(string value, StringBuilder builder)
        {
            builder.Append('"');
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (ch < ' ')
                            builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            builder.Append(ch);
                        break;
                }
            }
            builder.Append('"');
        }

        public static string Sha256Hex(byte[] data)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(data);
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // The loaded fixture, as replay consumes it
    // ═══════════════════════════════════════════════════════════════════════════════════════

    public sealed class FixtureTool
    {
        public string Name { get; }
        public string? Title { get; }
        public string? Description { get; }
        public JsonNode? InputSchema { get; }
        public JsonNode? OutputSchema { get; }
        public bool? ReadOnlyHint { get; }
        public bool? DestructiveHint { get; }
        public bool? IdempotentHint { get; }
        public bool? OpenWorldHint { get; }

        public FixtureTool(CObject line)
        {
            Name = line.GetString("name") ?? throw new FixtureFormatException("a tool line has no name");
            Title = line.GetString("title");
            Description = line.GetString("description");
            InputSchema = ToJsonNode(line.Get("inputSchema"));
            OutputSchema = ToJsonNode(line.Get("outputSchema"));
            ReadOnlyHint = ToBool(line.Get("readOnlyHint"));
            DestructiveHint = ToBool(line.Get("destructiveHint"));
            IdempotentHint = ToBool(line.Get("idempotentHint"));
            OpenWorldHint = ToBool(line.Get("openWorldHint"));
        }

        static bool? ToBool(CNode? node) => node is CBool boolean ? boolean.Value : (bool?)null;

        static JsonNode? ToJsonNode(CNode? node)
            => node == null || node is CNull ? null : JsonNode.Parse(CanonicalJson.Write(node));
    }

    public sealed class Fixture
    {
        public string Path { get; }
        public IReadOnlyList<FixtureTool> Tools { get; }

        /// <summary>tool name → (args_hash → the recorded response envelope).</summary>
        readonly Dictionary<string, Dictionary<string, CObject>> _calls;

        public Fixture(string path, IReadOnlyList<FixtureTool> tools, Dictionary<string, Dictionary<string, CObject>> calls)
        {
            Path = path;
            Tools = tools;
            _calls = calls;
        }

        public int CallCount => _calls.Values.Sum(byHash => byHash.Count);

        public bool HasCallsFor(string name) => _calls.ContainsKey(name);

        public CObject? FindResponse(string name, string argsHash)
        {
            if (!_calls.TryGetValue(name, out var byHash))
                return null;
            return byHash.TryGetValue(argsHash, out var response) ? response : null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // F1–F4: reading, canonicalising and loading a fixture
    // ═══════════════════════════════════════════════════════════════════════════════════════

    public static class FixtureLoader
    {
        /// <summary>The only fixture schema this build understands (F1).</summary>
        public const int Schema = 1;

        /// <summary>F4: a canonical response above this is stored as its hash.</summary>
        public const int MaxResponseBytes = 32 * 1024;

        /// <summary>F4: a whole canonical fixture above this fails canonicalisation.</summary>
        public const int MaxFixtureBytes = 256 * 1024;

        public const string RecorderInProcess = "in-process";
        public const string RecorderMcpClient = "mcp-client";

        /// <summary>F3: meta fields replaced by a placeholder in the comparison form.</summary>
        internal static readonly KeyValuePair<string, string>[] ProvenanceMeta =
        {
            new KeyValuePair<string, string>("recorded_at", "<ts>"),
            new KeyValuePair<string, string>("plugin_version", "<version>"),
            new KeyValuePair<string, string>("mcp_plugin_version", "<version>"),
        };

        /// <summary>F3: additionally neutralised under <c>--cross-recorder</c>.</summary>
        internal static readonly KeyValuePair<string, string>[] CrossRecorderMeta =
        {
            new KeyValuePair<string, string>("recorder", "<recorder>"),
        };

        internal static readonly string[] ContentBlockFields = { "type", "text", "data", "mimeType", "resource" };

        internal static readonly string[] ToolFields =
        {
            "title", "description", "inputSchema", "outputSchema",
            "readOnlyHint", "destructiveHint", "idempotentHint", "openWorldHint",
        };

        // ── F3 masking table. Mirrors scripts/chain_fixture.py exactly, INCLUDING the order. ──
        static readonly Regex ReTimestamp = new Regex(
            @"\d{4}-\d{2}-\d{2}[Tt ]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:[Zz]|[+-]\d{2}:?\d{2})?", RegexOptions.Compiled);
        static readonly Regex ReGuid = new Regex(
            @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);
        static readonly Regex ReWindowsPath = new Regex(
            @"(?<![A-Za-z0-9])[A-Za-z]:[\\/][^\s""'<>|]*", RegexOptions.Compiled);
        static readonly Regex RePosixPath = new Regex(
            @"(?<![A-Za-z0-9._\-])/(?:Users|home|tmp|var|private|opt|mnt|Volumes)(?![A-Za-z0-9])[^\s""'<>|]*",
            RegexOptions.Compiled);
        static readonly Regex ReUrlPort = new Regex(
            @"(://[^\s/:""'<>|]+):\d{1,5}", RegexOptions.Compiled);
        static readonly Regex ReDurationMs = new Regex(
            @"\b\d+(?:\.\d+)?\s*ms\b", RegexOptions.Compiled);

        static readonly Dictionary<string, string> MaskByKey = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["pid"] = "<pid>",
            ["process_id"] = "<pid>",
            ["processId"] = "<pid>",
            ["port"] = "<port>",
            ["duration_ms"] = "<ms>",
            ["durationMs"] = "<ms>",
            ["elapsed_ms"] = "<ms>",
            ["elapsedMs"] = "<ms>",
        };

        public static string MaskString(string value)
        {
            value = ReTimestamp.Replace(value, "<ts>");
            value = ReGuid.Replace(value, "<guid>");
            value = ReWindowsPath.Replace(value, "<path>");
            value = RePosixPath.Replace(value, "<path>");
            value = ReUrlPort.Replace(value, "$1:<port>");
            value = ReDurationMs.Replace(value, "<ms>");
            return value;
        }

        static CNode MaskNode(CNode node)
        {
            switch (node)
            {
                case CString text:
                    return new CString(MaskString(text.Value));
                case CArray array:
                    var maskedArray = new CArray();
                    foreach (var item in array.Items)
                        maskedArray.Items.Add(MaskNode(item));
                    return maskedArray;
                case CObject obj:
                    var maskedObject = new CObject();
                    foreach (var key in obj.Keys.ToList())
                    {
                        maskedObject.Set(
                            key,
                            MaskByKey.TryGetValue(key, out var placeholder)
                                ? new CString(placeholder)
                                : MaskNode(obj.Get(key)!));
                    }
                    return maskedObject;
                default:
                    return node;
            }
        }

        /// <summary>
        /// Replaces every JSON number by its raw token text, as a string, recursively.
        ///
        /// <para><b>Measured, not defensive.</b> The SignalR payload serializer this plugin family
        /// uses converts numbers to strings on the way to the plugin: a client that sends
        /// <c>{"kb": 1}</c> reaches the tool as <c>{"kb": "1"}</c>, and it applies inside nested
        /// objects too (<c>{"payload":{"number":7,"flag":true}}</c> arrives as
        /// <c>{"payload":{"number":"7","flag":true}}</c> — booleans and strings keep their kind,
        /// numbers do not). A hash taken over the CLIENT's view of the arguments could therefore
        /// never match one taken inside the plugin, and replay would miss every call carrying a
        /// number. Hashing this form makes the two views agree.</para>
        ///
        /// <para>It is a widening: <c>1</c> and <c>"1"</c> hash the same — precisely the
        /// identification the transport already makes, so nothing that could be told apart at
        /// replay time is lost. Only the HASH uses this form; the fixture keeps the arguments as
        /// recorded, so a diff still shows the battery's real values.</para>
        /// </summary>
        public static CNode NumbersAsText(CNode node)
        {
            switch (node)
            {
                case CNumber number:
                    return new CString(number.Raw);
                case CArray array:
                    var mappedArray = new CArray();
                    foreach (var item in array.Items)
                        mappedArray.Items.Add(NumbersAsText(item));
                    return mappedArray;
                case CObject obj:
                    var mappedObject = new CObject();
                    foreach (var key in obj.Keys.ToList())
                        mappedObject.Set(key, NumbersAsText(obj.Get(key)!));
                    return mappedObject;
                default:
                    return node;
            }
        }

        /// <summary>F1. sha256 of the canonical args JSON, prefixed with its algorithm.</summary>
        public static string ArgsHash(CNode? args)
        {
            var node = args as CObject ?? new CObject();
            var normalised = NumbersAsText(node);
            return "sha256:" + CanonicalJson.Sha256Hex(Encoding.UTF8.GetBytes(CanonicalJson.Write(normalised)));
        }

        public static string ArgsHash(IReadOnlyDictionary<string, JsonElement>? namedParameters)
        {
            var node = new CObject();
            if (namedParameters != null)
            {
                foreach (var pair in namedParameters)
                    node.Set(pair.Key, CanonicalJson.FromElement(pair.Value));
            }
            return ArgsHash(node);
        }

        // ── reading ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Splits a fixture's text into JSON objects. A trailing CR is tolerated on READ — a
        /// checkout that ignored .gitattributes must surface as the diff it is, not as a parse
        /// error — while every write is LF.
        /// </summary>
        public static List<CObject> ReadLines(string text, string path = "<fixture>")
        {
            var objects = new List<CObject>();
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (line.Trim().Length == 0)
                    continue;
                var parsed = CanonicalJson.Parse(line);
                if (parsed is not CObject obj)
                    throw new FixtureFormatException($"{path}:{i + 1} is not a JSON object");
                objects.Add(obj);
            }

            if (objects.Count == 0)
                throw new FixtureFormatException($"{path} is empty");
            return objects;
        }

        static void Split(List<CObject> objects, string path, out CObject meta, out List<CObject> tools, out List<CObject> calls)
        {
            meta = objects[0];
            if (meta.GetString("kind") != "meta")
                throw new FixtureFormatException($"{path}: the first line must be {{\"kind\":\"meta\"}}");

            var schema = meta.Get("schema");
            var schemaText = schema is CNumber number ? number.Raw : schema is CString str ? str.Value : "<missing>";
            if (schemaText != Schema.ToString(CultureInfo.InvariantCulture))
            {
                throw new FixtureFormatException(
                    $"{path}: schema mismatch - this build understands schema {Schema}, the fixture declares {schemaText}");
            }

            tools = new List<CObject>();
            calls = new List<CObject>();
            for (var i = 1; i < objects.Count; i++)
            {
                var entry = objects[i];
                var kind = entry.GetString("kind");
                if (kind == "tool")
                {
                    if (string.IsNullOrEmpty(entry.GetString("name")))
                        throw new FixtureFormatException($"{path}:{i + 1} tool line has no name");
                    tools.Add(entry);
                }
                else if (kind == "call")
                {
                    if (string.IsNullOrEmpty(entry.GetString("name")))
                        throw new FixtureFormatException($"{path}:{i + 1} call line has no name");
                    calls.Add(entry);
                }
                else
                {
                    throw new FixtureFormatException($"{path}:{i + 1} unknown kind '{kind}'");
                }
            }
        }

        // ── canonicalising ──────────────────────────────────────────────────────────────────

        static CObject CanonicalMeta(CObject meta, bool forDiff, bool crossRecorder)
        {
            var output = new CObject();
            output.Set("schema", new CNumber(Schema.ToString(CultureInfo.InvariantCulture)));
            output.Set("kind", new CString("meta"));
            foreach (var key in meta.Keys.ToList())
            {
                if (key == "schema" || key == "kind")
                    continue;
                output.Set(key, meta.Get(key)!);
            }

            if (!forDiff)
                return output;

            foreach (var pair in ProvenanceMeta)
            {
                if (output.Has(pair.Key))
                    output.Set(pair.Key, new CString(pair.Value));
            }
            if (crossRecorder)
            {
                foreach (var pair in CrossRecorderMeta)
                {
                    if (output.Has(pair.Key))
                        output.Set(pair.Key, new CString(pair.Value));
                }
            }
            return output;
        }

        static CObject CanonicalTool(CObject entry)
        {
            var output = new CObject();
            output.Set("kind", new CString("tool"));
            output.Set("name", new CString(entry.GetString("name")!));
            foreach (var field in ToolFields)
            {
                var value = entry.Get(field);
                if (value != null && value is not CNull)
                    output.Set(field, value);
            }
            return output;
        }

        internal static CObject CanonicalResponse(CNode? responseNode, bool dropErrorKind)
        {
            if (responseNode is not CObject response)
                throw new FixtureFormatException("a call's response must be a JSON object");

            if (response.Has("$hash"))
            {
                // An already-capped response passes through untouched: canonicalising a canonical
                // fixture must be a no-op, and re-hashing a hash would change it on every pass.
                if (!response.Has("$bytes"))
                    throw new FixtureFormatException("a capped response must carry both $hash and $bytes");
                var passthrough = new CObject();
                passthrough.Set("$hash", response.Get("$hash")!);
                passthrough.Set("$bytes", response.Get("$bytes")!);
                return passthrough;
            }

            var status = response.GetString("status");
            if (status != "success" && status != "error")
                throw new FixtureFormatException($"a call's response.status must be 'success' or 'error', got '{status}'");

            var output = new CObject();
            output.Set("status", new CString(status!));

            var blocks = response.Get("content");
            if (blocks != null && blocks is not CNull)
            {
                if (blocks is not CArray array)
                    throw new FixtureFormatException("a call's response.content must be an array");
                var normalised = new CArray();
                foreach (var item in array.Items)
                {
                    if (item is not CObject block)
                        throw new FixtureFormatException("a content block must be a JSON object");
                    var kept = new CObject();
                    foreach (var field in ContentBlockFields)
                    {
                        var value = block.Get(field);
                        if (value != null && value is not CNull)
                            kept.Set(field, value);
                    }
                    normalised.Items.Add(kept);
                }
                output.Set("content", normalised);
            }

            var structured = response.Get("structuredContent");
            if (structured != null && structured is not CNull)
                output.Set("structuredContent", structured);

            var errorKind = response.Get("errorKind");
            if (!dropErrorKind && errorKind != null && errorKind is not CNull)
                output.Set("errorKind", errorKind);

            var masked = (CObject)MaskNode(output);
            var encoded = Encoding.UTF8.GetBytes(CanonicalJson.Write(masked));
            if (encoded.Length > MaxResponseBytes)
            {
                var capped = new CObject();
                capped.Set("$hash", new CString("sha256:" + CanonicalJson.Sha256Hex(encoded)));
                capped.Set("$bytes", new CNumber(encoded.Length.ToString(CultureInfo.InvariantCulture)));
                return capped;
            }
            return masked;
        }

        static CObject CanonicalCall(CObject entry, bool dropErrorKind)
        {
            var argsNode = entry.Get("args");
            if (argsNode == null || argsNode is CNull)
                argsNode = new CObject();
            if (argsNode is not CObject args)
                throw new FixtureFormatException("a call's args must be a JSON object");

            var output = new CObject();
            output.Set("kind", new CString("call"));
            output.Set("name", new CString(entry.GetString("name")!));
            output.Set("args", args);
            // Always RECOMPUTED, never trusted — it is a derived field.
            output.Set("args_hash", new CString(ArgsHash(args)));
            output.Set("response", CanonicalResponse(entry.Get("response"), dropErrorKind));
            return output;
        }

        /// <summary>
        /// F2 + F3 + F4. Returns the canonical fixture TEXT: LF separated, one trailing LF.
        /// </summary>
        public static string Canonicalize(string text, bool forDiff = false, bool crossRecorder = false, string path = "<fixture>")
        {
            var objects = ReadLines(text, path);
            Split(objects, path, out var meta, out var toolEntries, out var callEntries);

            var lines = new List<string> { CanonicalJson.Write(CanonicalMeta(meta, forDiff, crossRecorder)) };

            // OrderBy / ThenBy, not List.Sort: LINQ's ordering is STABLE and Python's sorted() is
            // too, so two entries with the same key come out in the same order on both sides.
            foreach (var tool in toolEntries
                .Select(CanonicalTool)
                .OrderBy(tool => tool.GetString("name")!, CanonicalJson.Utf8Comparer.Instance))
            {
                lines.Add(CanonicalJson.Write(tool));
            }

            var dropErrorKind = forDiff && crossRecorder;
            foreach (var call in callEntries
                .Select(entry => CanonicalCall(entry, dropErrorKind))
                .OrderBy(call => call.GetString("name")!, CanonicalJson.Utf8Comparer.Instance)
                .ThenBy(call => call.GetString("args_hash")!, CanonicalJson.Utf8Comparer.Instance))
            {
                lines.Add(CanonicalJson.Write(call));
            }

            var canonical = string.Join("\n", lines) + "\n";
            var size = Encoding.UTF8.GetByteCount(canonical);
            if (size > MaxFixtureBytes)
            {
                throw new FixtureFormatException(
                    $"fixture too large: trim the battery ({size} bytes, cap {MaxFixtureBytes})");
            }
            return canonical;
        }

        // ── loading for replay ──────────────────────────────────────────────────────────────

        public static Fixture Load(string path)
        {
            string text;
            try
            {
                text = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (IOException ex)
            {
                throw new FixtureFormatException($"cannot read {path}: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new FixtureFormatException($"cannot read {path}: {ex.Message}");
            }

            return Parse(text, path);
        }

        public static Fixture Parse(string text, string path = "<fixture>")
        {
            // Canonicalise FIRST, then read the result back: replay must look calls up by exactly
            // the args_hash the canonical form defines, so deriving the lookup table from anything
            // else would let a hand-edited fixture replay under a hash the diff never sees.
            var canonical = Canonicalize(text, path: path);
            var objects = ReadLines(canonical, path);
            Split(objects, path, out _, out var toolEntries, out var callEntries);

            var tools = toolEntries.Select(entry => new FixtureTool(entry)).ToList();
            var calls = new Dictionary<string, Dictionary<string, CObject>>(StringComparer.Ordinal);
            foreach (var entry in callEntries)
            {
                var name = entry.GetString("name")!;
                var hash = entry.GetString("args_hash")!;
                if (!calls.TryGetValue(name, out var byHash))
                {
                    byHash = new Dictionary<string, CObject>(StringComparer.Ordinal);
                    calls[name] = byHash;
                }
                byHash[hash] = (CObject)entry.Get("response")!;
            }

            return new Fixture(path, tools, calls);
        }
    }
}
