/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
using System.Text.Json;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet;
using Microsoft.AspNetCore.SignalR;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Serialization
{
    /// <summary>
    /// <c>ResponseListTool</c> travels plugin → server over the SignalR hub, so its new
    /// skill members are only useful if they survive THAT serializer — not the ambient
    /// <c>JsonSerializerOptions</c> a hand-rolled test would reach for.
    /// <para>
    /// The options here are built by the production
    /// <see cref="SignalR_JsonConfiguration.ConfigureJsonSerializer"/> exactly as
    /// <c>HubConnectionProvider</c> (plugin side) and <c>ExtensionsMcpServerBuilder</c>
    /// (server side) build them. That configuration copies
    /// <c>DefaultIgnoreCondition</c> / <c>PropertyNamingPolicy</c> / <c>WriteIndented</c>
    /// / converters — but NOT <c>IncludeFields</c>, which is why the DTO members must be
    /// PROPERTIES. A field compiles, satisfies every in-process assertion, and then
    /// silently vanishes on the wire; the transport tests below are what catch that.
    /// </para>
    /// </summary>
    public class ResponseListToolSkillWireTests
    {
        const string SkillDescriptionText = "Creates a primitive object in the scene";
        const string SkillBodyText = "# GameObject create\n\nUse `path` to nest the new object.";

        static JsonSerializerOptions HubPayloadOptions()
        {
            var options = new JsonHubProtocolOptions();
            SignalR_JsonConfiguration.ConfigureJsonSerializer(new Reflector(), options);
            return options.PayloadSerializerOptions;
        }

        /// <summary>Resolves the on-the-wire member name under the hub's naming policy.</summary>
        static string WireName(JsonSerializerOptions options, string clrName)
            => options.PropertyNamingPolicy?.ConvertName(clrName) ?? clrName;

        [Fact]
        public void HubSerializer_TransportsSkillDescription()
        {
            var options = HubPayloadOptions();
            var source = new ResponseListTool
            {
                Name = "gameobject-create",
                InputSchema = Consts.MCP.EmptyInputSchema,
                SkillDescription = SkillDescriptionText
            };

            var json = JsonSerializer.Serialize(source, options);

            // Positive artifact on the WIRE, not merely on the round-tripped object (the
            // class summary above states why that distinction is the point of this file).
            using (var doc = JsonDocument.Parse(json))
            {
                doc.RootElement
                    .TryGetProperty(WireName(options, nameof(ResponseListTool.SkillDescription)), out var emitted)
                    .ShouldBeTrue($"skill description must be emitted on the hub payload. Payload was: {json}");
                emitted.GetString().ShouldBe(SkillDescriptionText);
            }

            var roundTripped = JsonSerializer.Deserialize<ResponseListTool>(json, options);
            roundTripped.ShouldNotBeNull();
            roundTripped!.SkillDescription.ShouldBe(SkillDescriptionText);
        }

        [Fact]
        public void HubSerializer_TransportsSkillBody()
        {
            var options = HubPayloadOptions();
            var source = new ResponseListTool
            {
                Name = "gameobject-create",
                InputSchema = Consts.MCP.EmptyInputSchema,
                SkillBody = SkillBodyText
            };

            var json = JsonSerializer.Serialize(source, options);

            using (var doc = JsonDocument.Parse(json))
            {
                doc.RootElement
                    .TryGetProperty(WireName(options, nameof(ResponseListTool.SkillBody)), out var emitted)
                    .ShouldBeTrue($"skill body must be emitted on the hub payload. Payload was: {json}");
                emitted.GetString().ShouldBe(SkillBodyText);
            }

            var roundTripped = JsonSerializer.Deserialize<ResponseListTool>(json, options);
            roundTripped.ShouldNotBeNull();
            roundTripped!.SkillBody.ShouldBe(SkillBodyText);
        }

        [Fact]
        public void HubSerializer_OmitsSkillMembers_WhenAbsent()
        {
            var options = HubPayloadOptions();
            var source = new ResponseListTool
            {
                Name = "gameobject-create",
                InputSchema = Consts.MCP.EmptyInputSchema
            };

            var json = JsonSerializer.Serialize(source, options);

            using var doc = JsonDocument.Parse(json);

            // Control: this payload really was produced (so the two absences below are a
            // statement about the skill members, not about an empty document).
            doc.RootElement
                .TryGetProperty(WireName(options, nameof(ResponseListTool.Name)), out var name)
                .ShouldBeTrue($"the payload must carry the tool name. Payload was: {json}");
            name.GetString().ShouldBe("gameobject-create");

            // OMITTED, not emitted-as-null: a `"SkillDescription": null` member would make
            // TryGetProperty succeed with JsonValueKind.Null, so this distinguishes the two.
            doc.RootElement
                .TryGetProperty(WireName(options, nameof(ResponseListTool.SkillDescription)), out _)
                .ShouldBeFalse($"absent skill description must not reach the wire at all. Payload was: {json}");
            doc.RootElement
                .TryGetProperty(WireName(options, nameof(ResponseListTool.SkillBody)), out _)
                .ShouldBeFalse($"absent skill body must not reach the wire at all. Payload was: {json}");
        }

        [Fact]
        public void HubSerializer_ReadsOldShapePayload_WithoutSkillMembers()
        {
            // Backward tolerance: a plugin built before this change sends a payload with
            // neither member. The server must still materialise the tool.
            var options = HubPayloadOptions();
            // `Enabled` is carried as FALSE on purpose. The member defaults to `true`, so
            // asserting it deserializes to `true` is satisfied by the initializer and holds
            // under every mutation, including one that stops binding the member at all.
            // `false` is the only value in the domain that distinguishes a real bind.
            var oldShape =
                "{\"" + WireName(options, nameof(ResponseListTool.Name)) + "\":\"gameobject-create\"," +
                "\"" + WireName(options, nameof(ResponseListTool.Enabled)) + "\":false," +
                "\"" + WireName(options, nameof(ResponseListTool.Description)) + "\":\"legacy\"}";

            var parsed = JsonSerializer.Deserialize<ResponseListTool>(oldShape, options);

            parsed.ShouldNotBeNull();
            parsed!.Name.ShouldBe("gameobject-create");
            parsed.Description.ShouldBe("legacy");
            parsed.Enabled.ShouldBeFalse();
            parsed.SkillDescription.ShouldBeNull();
            parsed.SkillBody.ShouldBeNull();
        }

        [Fact]
        public void HubSerializer_IgnoresUnknownMembers_SoNewKeysAreForwardTolerant()
        {
            // Forward tolerance: the mirror of the case above. An OLDER peer deserializing a
            // payload that carries members it does not know must not throw — which is what
            // makes adding the two skill members a non-breaking wire change.
            var options = HubPayloadOptions();
            var newerShape =
                "{\"" + WireName(options, nameof(ResponseListTool.Name)) + "\":\"gameobject-create\"," +
                "\"someMemberFromAFuturePeer\":\"value\"}";

            var parsed = JsonSerializer.Deserialize<ResponseListTool>(newerShape, options);

            parsed.ShouldNotBeNull();
            parsed!.Name.ShouldBe("gameobject-create");
        }
    }
}
