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
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;

namespace com.IvanMurzak.McpPlugin.NullEngine.Replay
{
    /// <summary>
    /// F6. One <see cref="IRunTool"/> per <c>tool</c> line of a fixture: it publishes the recorded
    /// name / title / description / schemas / hints, and answers a call with the response recorded
    /// for the SAME <c>(name, args_hash)</c>.
    ///
    /// <para>Modelled on <see cref="ProxyTool"/> — schemas supplied externally at runtime, no
    /// reflected method behind it — but deliberately its own type: a proxy forwards to a handler
    /// that can do anything, while this one can only ever return something the fixture already
    /// contains, and every way of NOT finding it is a distinct, named error. That is the property
    /// a replay host needs: <b>never a silent success</b>. A consumer that reaches a
    /// <c>replay-*</c> error has asked for something the fixture does not cover, and the message
    /// says which of the three cases it is.</para>
    ///
    /// <para><b>Not registered:</b> a tool absent from the fixture is not registered at all, so a
    /// call for it never reaches this class — <c>McpToolManager.RunCallTool</c> answers
    /// <c>Tool with Name '…' not found.</c> and the MCP client sees <c>isError: true</c>. That is
    /// the third miss case; the two below are the ones a registered tool can produce.</para>
    /// </summary>
    public sealed class ReplayTool : IRunTool
    {
        /// <summary>Prefix of the error a call for a tool the fixture never exercised returns.</summary>
        public const string MissPrefix = "replay-miss: ";

        /// <summary>Prefix of the error a call with arguments the fixture never recorded returns.</summary>
        public const string MissArgsPrefix = "replay-miss-args: ";

        /// <summary>Prefix of the error a call whose recorded response was capped (F4) returns.</summary>
        public const string OversizePrefix = "replay-oversize: ";

        readonly Fixture _fixture;
        int? _cachedTokenCount;

        public string Name { get; }
        public string? Title { get; }
        public string? Description { get; }

        /// <summary>Not part of the F1 tool line: skill metadata reaches <c>tools/list</c> only
        /// behind an opt-in header, so a fixture recorded without it could never carry one.</summary>
        public string? SkillDescription => null;

        /// <summary>See <see cref="SkillDescription"/>.</summary>
        public string? SkillBody => null;

        public JsonNode? InputSchema { get; }
        public JsonNode? OutputSchema { get; }
        public bool? ReadOnlyHint { get; }
        public bool? DestructiveHint { get; }
        public bool? IdempotentHint { get; }
        public bool? OpenWorldHint { get; }

        public bool Enabled { get; set; } = true;

        /// <summary>
        /// The same chars/4 approximation <c>RunTool</c> and <c>ProxyTool</c> report, recomputed
        /// here because the helper that implements it (<c>ToolTokenCount</c>) is <c>internal</c> to
        /// McpPlugin and this assembly only references it. The count is NOT part of the T2
        /// contract — a fixture records no token counts — so the duplication is a convenience for
        /// <c>IToolManager.EnabledToolsTokenCount</c>, not a claim anything is asserted on.
        /// </summary>
        public int TokenCount
        {
            get
            {
                if (_cachedTokenCount.HasValue)
                    return _cachedTokenCount.Value;

                try
                {
                    var document = new JsonObject();
                    if (!string.IsNullOrEmpty(Name))
                        document["name"] = Name;
                    if (!string.IsNullOrEmpty(Title))
                        document["title"] = Title;
                    if (!string.IsNullOrEmpty(Description))
                        document["description"] = Description;
                    if (InputSchema != null)
                        document["inputSchema"] = JsonNode.Parse(InputSchema.ToJsonString());
                    if (OutputSchema != null)
                        document["outputSchema"] = JsonNode.Parse(OutputSchema.ToJsonString());

                    _cachedTokenCount = (int)Math.Ceiling(document.ToJsonString().Length / 4.0);
                }
                catch (Exception)
                {
                    // Same silent resilience as ProxyTool: one unserializable schema must not
                    // poison IToolManager.EnabledToolsTokenCount, which sums over every tool.
                    _cachedTokenCount = 0;
                }
                return _cachedTokenCount.Value;
            }
        }

        ReplayTool(FixtureTool tool, Fixture fixture)
        {
            _fixture = fixture;
            Name = tool.Name;
            Title = tool.Title;
            Description = tool.Description;
            InputSchema = tool.InputSchema;
            OutputSchema = tool.OutputSchema;
            ReadOnlyHint = tool.ReadOnlyHint;
            DestructiveHint = tool.DestructiveHint;
            IdempotentHint = tool.IdempotentHint;
            OpenWorldHint = tool.OpenWorldHint;
        }

        /// <summary>
        /// Registers one <see cref="ReplayTool"/> per fixture tool line and returns how many were
        /// added. Called BEFORE <c>Connect</c> so the first catalogue the server ever sees is the
        /// fixture's; registering afterwards would publish an empty catalogue and then correct it.
        /// </summary>
        public static int RegisterAll(IToolManager? toolManager, Fixture fixture)
        {
            if (toolManager == null)
                throw new ArgumentNullException(nameof(toolManager));
            if (fixture == null)
                throw new ArgumentNullException(nameof(fixture));

            var registered = 0;
            foreach (var tool in fixture.Tools)
            {
                if (toolManager.AddTool(tool.Name, new ReplayTool(tool, fixture)))
                    registered++;
            }
            return registered;
        }

        public Task<ResponseCallTool> Run(
            string requestId,
            IReadOnlyDictionary<string, JsonElement>? namedParameters,
            CancellationToken cancellationToken = default)
        {
            var hash = FixtureLoader.ArgsHash(namedParameters);

            if (!_fixture.HasCallsFor(Name))
            {
                return Task.FromResult(ResponseCallTool.Error(
                    MissPrefix + Name, ResponseErrorKind.NotFound));
            }

            var response = _fixture.FindResponse(Name, hash);
            if (response == null)
            {
                return Task.FromResult(ResponseCallTool.Error(
                    MissArgsPrefix + Name + " " + hash, ResponseErrorKind.NotFound));
            }

            if (response.Has("$hash"))
            {
                // F4: the payload was replaced by its digest at record time, so there is nothing to
                // serve. Answering with the digest would be a silent success on wrong bytes.
                return Task.FromResult(ResponseCallTool.Error(
                    OversizePrefix + (response.GetString("$hash") ?? "sha256:?"), ResponseErrorKind.Unavailable));
            }

            return Task.FromResult(Materialize(response));
        }

        /// <summary>
        /// Rebuilds a <see cref="ResponseCallTool"/> that the SERVER will project back into exactly
        /// the recorded MCP result.
        ///
        /// <para>The error branch goes through <see cref="ResponseCallTool.Error"/> rather than
        /// copying the recorded content blocks, because that is the shape the server's own error
        /// path produces: <c>ToolRouter.Call</c> discards the response's content on an error and
        /// rebuilds a single text block from the packed message
        /// (<c>McpPlugin.Server/src/Mcp/Tool/ToolRouter.Call.cs:88-89</c>). Replaying the recorded
        /// blocks verbatim would round-trip differently for any error carrying more than one.</para>
        /// </summary>
        internal static ResponseCallTool Materialize(CObject response)
        {
            var status = response.GetString("status");
            var content = ReadContent(response);

            if (status == "error")
            {
                var message = FirstText(content) ?? "Tool execution error.";
                return ResponseCallTool.Error(message, ParseErrorKind(response.GetString("errorKind")));
            }

            var structured = response.Get("structuredContent");
            return new ResponseCallTool
            {
                Status = ResponseStatus.Success,
                Content = content,
                StructuredContent = structured == null || structured is CNull
                    ? null
                    : JsonNode.Parse(CanonicalJson.Write(structured)),
            };
        }

        static ResponseErrorKind ParseErrorKind(string? recorded)
        {
            if (string.IsNullOrEmpty(recorded))
                return ResponseErrorKind.Internal;
            return Enum.TryParse<ResponseErrorKind>(recorded, ignoreCase: true, out var parsed)
                ? parsed
                : ResponseErrorKind.Internal;
        }

        static string? FirstText(List<ContentBlock> content)
        {
            foreach (var block in content)
            {
                if (block.Type == Common.Consts.ContentType.Text && !string.IsNullOrEmpty(block.Text))
                    return block.Text;
            }
            return null;
        }

        static List<ContentBlock> ReadContent(CObject response)
        {
            var content = new List<ContentBlock>();
            if (response.Get("content") is not CArray array)
                return content;

            foreach (var item in array.Items)
            {
                if (item is not CObject block)
                    continue;

                var type = block.GetString("type") ?? Common.Consts.ContentType.Text;
                var restored = new ContentBlock
                {
                    Type = type,
                    Text = block.GetString("text"),
                    Data = block.GetString("data"),
                    MimeType = block.GetString("mimeType"),
                };

                if (block.Get("resource") is CObject resource)
                {
                    restored.Resource = new ResponseResourceContent(
                        uri: resource.GetString("uri") ?? string.Empty,
                        mimeType: resource.GetString("mimeType"),
                        text: resource.GetString("text"),
                        blob: resource.GetString("blob"));
                }

                content.Add(restored);
            }
            return content;
        }

        /// <summary>A one-line summary a host can print, so a replay run says what it loaded.</summary>
        public static string Describe(Fixture fixture)
            => string.Format(
                CultureInfo.InvariantCulture,
                "tools={0} calls={1} fixture={2}",
                fixture.Tools.Count,
                fixture.CallCount,
                fixture.Path);
    }
}
