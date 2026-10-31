/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet;
using Microsoft.AspNetCore.SignalR;
using Shouldly;
using Xunit;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Serialization
{
    /// <summary>
    /// A tool that throws on the request's content now answers BadRequest + HttpStatusCode 422 (it was Internal).
    /// Servers on McpPlugin 8.13 / 8.14 (GameDev-MCP-Server 9.2.x / 9.3.0) stay in production next to new
    /// plugins, so the response must be one THEY read: no new <see cref="ResponseErrorKind"/> member (the kind
    /// travels by name and an unknown name fails their deserializer), only fields they already have. The legacy
    /// types below are frozen copies of the 8.13 / 8.14 wire shape and direct-endpoint mapping
    /// (<c>DirectHttpErrorMapper</c>, unchanged since McpPlugin 7.0.0).
    /// </summary>
    [Collection("McpPlugin")]
    public class ToolFailureStatusWireTests
    {
        static JsonSerializerOptions HubPayloadOptions()
        {
            var options = new JsonHubProtocolOptions();
            SignalR_JsonConfiguration.ConfigureJsonSerializer(new Reflector(), options);
            return options.PayloadSerializerOptions;
        }

        static async Task<string> ToolFailureOnTheWire()
        {
            var builder = new McpPluginBuilder(new Version());
            var method = typeof(CompilingTool).GetMethod(nameof(CompilingTool.Execute));
            builder.WithTool("script-execute", "Execute script", typeof(CompilingTool), method!);
            var plugin = builder.Build(new Reflector());

            var response = await plugin.McpManager.ToolManager!.RunCallTool(
                new RequestCallTool("request-1", "script-execute", new Dictionary<string, JsonElement>()));

            return JsonSerializer.Serialize(response, HubPayloadOptions());
        }

        [Fact]
        public async Task ToolFailure_TravelsAsAKindOldServersKnow_WithStatus422()
        {
            var json = await ToolFailureOnTheWire();
            var options = HubPayloadOptions();

            using var doc = JsonDocument.Parse(json);
            var errorKind = doc.RootElement.GetProperty(options.PropertyNamingPolicy?.ConvertName(nameof(ResponseData.ErrorKind)) ?? nameof(ResponseData.ErrorKind));
            errorKind.ValueKind.ShouldBe(JsonValueKind.String, "the kind travels by name: " + json);
            Enum.GetNames(typeof(LegacyResponseErrorKind)).ShouldContain(errorKind.GetString()!, "an 8.13/8.14 server must know the kind: " + json);
            doc.RootElement.GetProperty(options.PropertyNamingPolicy?.ConvertName(nameof(ResponseData.HttpStatusCode)) ?? nameof(ResponseData.HttpStatusCode))
                .GetInt32().ShouldBe(422);
        }

        [Fact]
        public async Task OldServer_DeserializesToolFailure_AndItsDirectEndpointAnswers422WithTheToolMessage()
        {
            var json = await ToolFailureOnTheWire();

            // Strict like an old server: names only, the old member set.
            var legacy = JsonSerializer.Deserialize<LegacyResponseData>(json, LegacyOptions(HubPayloadOptions()))!;

            legacy.Status.ShouldBe(LegacyResponseStatus.Error);
            legacy.ErrorKind.ShouldBe(LegacyResponseErrorKind.BadRequest);
            legacy.HttpStatusCode.ShouldBe(422);
            legacy.Message.ShouldBe("Tool execution failed for 'Execute script': Compilation failed: CS1002 ; expected");
            LegacyDirectHttpErrorMapper.GetStatusCode(legacy, 500).ShouldBe(422);
        }

        [Fact]
        public void ServerIgnoringTheStatus_StillBlamesTheRequest_With400()
        {
            var legacy = new LegacyResponseData { Status = LegacyResponseStatus.Error, ErrorKind = LegacyResponseErrorKind.BadRequest };

            LegacyDirectHttpErrorMapper.GetStatusCode(legacy, 500).ShouldBe(400);
        }

        static JsonSerializerOptions LegacyOptions(JsonSerializerOptions hub)
        {
            var options = new JsonSerializerOptions(hub);
            options.Converters.Insert(0, new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
            return options;
        }

        sealed class CompilingTool
        {
            public string Execute() => throw new Exception("Compilation failed: CS1002 ; expected");
        }

        // ── frozen McpPlugin 8.13 / 8.14 shapes ──

        enum LegacyResponseErrorKind { None, BadRequest, NotFound, Conflict, Timeout, Unavailable, Internal }

        enum LegacyResponseStatus { Error, Success, Processing }

        sealed class LegacyResponseData
        {
            public string RequestID { get; set; } = string.Empty;
            public LegacyResponseStatus Status { get; set; }
            public string? Message { get; set; }
            public LegacyResponseErrorKind ErrorKind { get; set; }
            public int? HttpStatusCode { get; set; }
        }

        static class LegacyDirectHttpErrorMapper
        {
            public static int GetStatusCode(LegacyResponseData response, int fallbackStatusCode)
            {
                if (response.HttpStatusCode is >= 400 and <= 599)
                    return response.HttpStatusCode.Value;

                return response.ErrorKind switch
                {
                    LegacyResponseErrorKind.BadRequest => 400,
                    LegacyResponseErrorKind.NotFound => 404,
                    LegacyResponseErrorKind.Conflict => 409,
                    LegacyResponseErrorKind.Unavailable => 503,
                    LegacyResponseErrorKind.Timeout => 504,
                    LegacyResponseErrorKind.Internal => 500,
                    _ => fallbackStatusCode
                };
            }
        }
    }
}
