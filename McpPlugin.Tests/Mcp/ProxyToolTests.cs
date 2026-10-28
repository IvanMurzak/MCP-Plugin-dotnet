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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.DependencyInjection;
using R3;
using Shouldly;
using Xunit;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    [Collection("McpPlugin")]
    public class ProxyToolTests
    {
        private readonly Version _version = new Version();

        private const string InputSchemaJson =
            "{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"string\"}},\"required\":[\"value\"]}";
        private const string OutputSchemaJson =
            "{\"type\":\"object\",\"properties\":{\"ok\":{\"type\":\"boolean\"}}}";

        private static Func<string, IReadOnlyDictionary<string, JsonElement>?, CancellationToken, Task<ResponseCallTool>> NoopHandler()
            => (_, _, _) => Task.FromResult(ResponseCallTool.Success("noop"));

        [Fact]
        public void WithDynamicToolFactory_ShouldRegisterFactoryAsSingleton()
        {
            // Arrange
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version);
            builder.WithDynamicToolFactory();

            // Act
            builder.Build(reflector);
            var factory1 = builder.ServiceProvider!.GetRequiredService<IDynamicToolFactory>();
            var factory2 = builder.ServiceProvider!.GetRequiredService<IDynamicToolFactory>();

            // Assert
            factory1.ShouldNotBeNull();
            factory1.ShouldBeOfType<ProxyToolFactory>();
            factory2.ShouldBeSameAs(factory1); // singleton
        }

        [Fact]
        public void CreateProxyTool_ShouldReturnIRunTool_WithSettableVisibleDefaultingTrue()
        {
            // Arrange
            IDynamicToolFactory factory = new ProxyToolFactory();

            // Act
            var tool = factory.CreateProxyTool(
                name: "proxy-create",
                title: "Proxy Create",
                description: "A runtime tool.",
                skillDescription: null,
                skillBody: null,
                inputSchema: JsonNode.Parse(InputSchemaJson),
                outputSchema: JsonNode.Parse(OutputSchemaJson),
                readOnlyHint: true,
                destructiveHint: false,
                idempotentHint: null,
                openWorldHint: null,
                handler: NoopHandler());

            // Assert
            tool.ShouldNotBeNull();
            tool.ShouldBeAssignableTo<IRunTool>();
            tool.Name.ShouldBe("proxy-create");
            tool.Visible.ShouldBeTrue(); // default true

            tool.Visible = false; // settable
            tool.Visible.ShouldBeFalse();
        }

        [Fact]
        public void Constructor_ShouldThrow_WhenNameOrHandlerIsNull()
        {
            Should.Throw<ArgumentNullException>(() => new ProxyTool(
                name: null!, title: null, description: null, skillDescription: null, skillBody: null,
                inputSchema: null, outputSchema: null,
                readOnlyHint: null, destructiveHint: null, idempotentHint: null, openWorldHint: null,
                handler: NoopHandler()));

            Should.Throw<ArgumentNullException>(() => new ProxyTool(
                name: "n", title: null, description: null, skillDescription: null, skillBody: null,
                inputSchema: null, outputSchema: null,
                readOnlyHint: null, destructiveHint: null, idempotentHint: null, openWorldHint: null,
                handler: null!));
        }

        [Fact]
        public async Task ProxyTool_Lifecycle_AddListChangedCallRemove_RoutesThroughHandler()
        {
            // Arrange
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version);
            builder.WithDynamicToolFactory();
            var plugin = builder.Build(reflector);
            var toolManager = plugin.McpManager.ToolManager!;
            var factory = builder.ServiceProvider!.GetRequiredService<IDynamicToolFactory>();

            var updates = 0;
            using var sub = toolManager.OnToolsUpdated.Subscribe(_ => updates++);

            var handlerInvoked = false;
            string? capturedRequestId = null;
            IReadOnlyDictionary<string, JsonElement>? capturedArgs = null;

            var proxy = factory.CreateProxyTool(
                name: "proxy-echo",
                title: "Proxy Echo",
                description: "Echoes the value argument back.",
                skillDescription: null,
                skillBody: null,
                inputSchema: JsonNode.Parse(InputSchemaJson),
                outputSchema: JsonNode.Parse(OutputSchemaJson),
                readOnlyHint: true,
                destructiveHint: false,
                idempotentHint: true,
                openWorldHint: false,
                handler: (requestId, args, _) =>
                {
                    handlerInvoked = true;
                    capturedRequestId = requestId;
                    capturedArgs = args;
                    return Task.FromResult(ResponseCallTool.Success("proxy-handled"));
                });

            // Act + Assert — AddTool fires list_changed
            toolManager.AddTool("proxy-echo", proxy).ShouldBeTrue();
            updates.ShouldBe(1);
            toolManager.HasTool("proxy-echo").ShouldBeTrue();

            // The tool appears in the listing with its externally supplied schema
            var listResponse = await toolManager.RunListTool(new RequestListTool());
            listResponse.Status.ShouldBe(ResponseStatus.Success);
            var listed = Array.Find(listResponse.Value!, t => t.Name == "proxy-echo");
            listed.ShouldNotBeNull();
            listed!.Title.ShouldBe("Proxy Echo");
            listed.Visible.ShouldBeTrue();

            // Call dispatch routes through the proxy handler
            var args = new Dictionary<string, JsonElement>
            {
                ["value"] = JsonSerializer.SerializeToElement("hello")
            };
            var callResponse = await toolManager.RunCallTool(new RequestCallTool("req-1", "proxy-echo", args));

            handlerInvoked.ShouldBeTrue();
            capturedRequestId.ShouldBe("req-1");
            capturedArgs.ShouldNotBeNull();
            capturedArgs!.ContainsKey("value").ShouldBeTrue();
            callResponse.Status.ShouldBe(ResponseStatus.Success);
            callResponse.Value.ShouldNotBeNull();
            callResponse.Value!.GetMessage().ShouldBe("proxy-handled");

            // RemoveTool fires list_changed again and the tool is gone
            toolManager.RemoveTool("proxy-echo").ShouldBeTrue();
            updates.ShouldBe(2);
            toolManager.HasTool("proxy-echo").ShouldBeFalse();
        }

        [Fact]
        public async Task ProxyTool_HiddenToggle_FiltersOutOfVisibleView()
        {
            // Arrange
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version);
            builder.WithDynamicToolFactory();
            var plugin = builder.Build(reflector);
            var toolManager = plugin.McpManager.ToolManager!;
            var factory = builder.ServiceProvider!.GetRequiredService<IDynamicToolFactory>();

            var enabledProxy = factory.CreateProxyTool(
                "proxy-enabled", "Enabled", "Enabled proxy.", null, null,
                JsonNode.Parse(InputSchemaJson), JsonNode.Parse(OutputSchemaJson),
                null, null, null, null, NoopHandler());

            var toggledProxy = factory.CreateProxyTool(
                "proxy-toggled", "Toggled", "Toggled proxy.", null, null,
                JsonNode.Parse(InputSchemaJson), JsonNode.Parse(OutputSchemaJson),
                null, null, null, null, NoopHandler());

            // Act + Assert
            toolManager.AddTool("proxy-enabled", enabledProxy).ShouldBeTrue();
            toolManager.AddTool("proxy-toggled", toggledProxy).ShouldBeTrue();

            toolManager.TotalToolsCount.ShouldBe(2);
            toolManager.VisibleToolsCount.ShouldBe(2);
            var bothEnabledTokens = toolManager.VisibleToolsTokenCount;

            // Hide one — it must drop out of the visible view (count + token sum) but stay registered
            toolManager.SetToolVisible("proxy-toggled", false).ShouldBeTrue();

            toolManager.IsToolVisible("proxy-toggled").ShouldBeFalse();
            toolManager.TotalToolsCount.ShouldBe(2);
            toolManager.VisibleToolsCount.ShouldBe(1);
            toolManager.VisibleToolsTokenCount.ShouldBe(bothEnabledTokens - toggledProxy.TokenCount);

            // The listing still includes it, flagged hidden
            var listResponse = await toolManager.RunListTool(new RequestListTool());
            var listed = Array.Find(listResponse.Value!, t => t.Name == "proxy-toggled");
            listed.ShouldNotBeNull();
            listed!.Visible.ShouldBeFalse();

            // Showing it again restores it
            toolManager.SetToolVisible("proxy-toggled", true).ShouldBeTrue();
            toolManager.VisibleToolsCount.ShouldBe(2);
            toolManager.VisibleToolsTokenCount.ShouldBe(bothEnabledTokens);
        }

        [Fact]
        public void TokenCount_ShouldMatch_RunToolCharsOver4Formula()
        {
            // Arrange
            const string name = "proxy-token";
            const string title = "Proxy Token";
            const string description = "A proxy tool used to verify token-count parity.";

            var proxy = new ProxyTool(
                name, title, description, null, null,
                JsonNode.Parse(InputSchemaJson), JsonNode.Parse(OutputSchemaJson),
                null, null, null, null, NoopHandler());

            // Independently replicate the chars/4 formula over the same JSON shape with fresh node instances.
            var expectedObject = new JsonObject
            {
                ["name"] = name,
                ["title"] = title,
                ["description"] = description,
                ["inputSchema"] = JsonNode.Parse(JsonNode.Parse(InputSchemaJson)!.ToJsonString()),
                ["outputSchema"] = JsonNode.Parse(JsonNode.Parse(OutputSchemaJson)!.ToJsonString())
            };
            var expected = (int)Math.Ceiling(expectedObject.ToJsonString().Length / 4.0);

            // Act + Assert
            proxy.TokenCount.ShouldBeGreaterThan(0);
            proxy.TokenCount.ShouldBe(expected);

            // Cached: repeated reads return the same value.
            proxy.TokenCount.ShouldBe(expected);
        }

        [Fact]
        public void TokenCount_ShouldBeHigher_WhenDescriptionPresent()
        {
            // Arrange
            var withDescription = new ProxyTool(
                "proxy-desc", "Proxy", "A reasonably long description that adds characters.", null, null,
                JsonNode.Parse(InputSchemaJson), JsonNode.Parse(OutputSchemaJson),
                null, null, null, null, NoopHandler());

            var withoutDescription = new ProxyTool(
                "proxy-desc", "Proxy", null, null, null,
                JsonNode.Parse(InputSchemaJson), JsonNode.Parse(OutputSchemaJson),
                null, null, null, null, NoopHandler());

            // Act + Assert
            withDescription.TokenCount.ShouldBeGreaterThan(withoutDescription.TokenCount);
        }
    }
}
