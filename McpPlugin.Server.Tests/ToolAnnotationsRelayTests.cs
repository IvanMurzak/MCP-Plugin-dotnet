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
using ModelContextProtocol;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// A plugin tool's title and hints reach MCP <c>tools/list</c> unchanged through
    /// <c>ExtensionsTool.ToTool()</c> — the projection <c>ToolRouter.ListAll</c> maps every plugin tool
    /// through. An unset hint stays unset (omitted on the wire); the relay never invents a default.
    /// </summary>
    public class ToolAnnotationsRelayTests
    {
        [Fact]
        public void ToTool_CopiesTitleToTopLevelAndAnnotations_AndEveryHint()
        {
            var tool = new ResponseListTool
            {
                Name = "scene-save",
                Title = "Save scene",
                InputSchema = Consts.MCP.EmptyInputSchema,
                ReadOnlyHint = false,
                DestructiveHint = true,
                IdempotentHint = true,
                OpenWorldHint = false
            }.ToTool();

            tool.Title.ShouldBe("Save scene");
            tool.Annotations.ShouldNotBeNull();
            tool.Annotations!.Title.ShouldBe("Save scene");
            tool.Annotations.ReadOnlyHint.ShouldBe(false);
            tool.Annotations.DestructiveHint.ShouldBe(true);
            tool.Annotations.IdempotentHint.ShouldBe(true);
            tool.Annotations.OpenWorldHint.ShouldBe(false);
        }

        [Fact]
        public void ToTool_UnsetHints_AreOmittedFromTheWire()
        {
            var tool = new ResponseListTool
            {
                Name = "scene-list",
                Title = "List scenes",
                InputSchema = Consts.MCP.EmptyInputSchema,
                ReadOnlyHint = true
            }.ToTool();

            var json = JsonSerializer.SerializeToElement(tool, McpJsonUtilities.DefaultOptions);
            json.GetProperty("title").GetString().ShouldBe("List scenes");

            var annotations = json.GetProperty("annotations");
            annotations.GetProperty("title").GetString().ShouldBe("List scenes");
            annotations.GetProperty("readOnlyHint").GetBoolean().ShouldBeTrue();
            annotations.TryGetProperty("destructiveHint", out _).ShouldBeFalse();
            annotations.TryGetProperty("idempotentHint", out _).ShouldBeFalse();
            annotations.TryGetProperty("openWorldHint", out _).ShouldBeFalse();
        }
    }
}
