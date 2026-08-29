/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Tests.Data.Annotations;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using com.IvanMurzak.ReflectorNet;
using Shouldly;
using Xunit;
using Xunit.Abstractions;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    /// <summary>
    /// <c>tools/list</c> must carry each tool's optional skill metadata
    /// (<c>[AiSkillDescription]</c> / <c>[AiSkillBody]</c>) so the MCP server can
    /// re-emit it as <c>_meta.skillDescription</c> / <c>_meta.skillBody</c>. The
    /// accessors already existed on <c>IRunTool</c>; what is under test here is that
    /// <c>McpToolManager.RunListTool</c> actually COPIES them into the response DTO.
    /// </summary>
    [Collection("McpPlugin")]
    public class McpBuilderTests_ToolSkillMetadata
    {
        private readonly ITestOutputHelper _output;
        private readonly XunitTestOutputLoggerProvider _loggerProvider;
        private readonly Version version = new Version();

        public McpBuilderTests_ToolSkillMetadata(ITestOutputHelper output)
        {
            _output = output;
            _loggerProvider = new XunitTestOutputLoggerProvider(output);
        }

        private async Task<ResponseListTool> GetTool(string toolName)
        {
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(version, _loggerProvider)
                .AddLogging(b => b.AddXunitTestOutput(_output))
                .WithTools(typeof(SkillAnnotatedToolClass));

            var mcpPlugin = mcpPluginBuilder.Build(reflector);
            var response = await mcpPlugin.McpManager.ToolManager!.RunListTool(new RequestListTool());

            response.ShouldNotBeNull();
            response.Status.ShouldBe(ResponseStatus.Success);
            response.Value.ShouldNotBeNull();

            var tool = System.Linq.Enumerable.FirstOrDefault(response.Value!, t => t.Name == toolName);
            tool.ShouldNotBeNull($"Tool '{toolName}' should be registered");
            return tool!;
        }

        [Fact]
        public async Task ListTool_CopiesSkillDescription_WhenAttributePresent()
        {
            var tool = await GetTool("skill-tool-both");

            tool.SkillDescription.ShouldBe(SkillAnnotatedToolClass.BothDescription);
        }

        [Fact]
        public async Task ListTool_CopiesSkillBody_WhenAttributePresent()
        {
            var tool = await GetTool("skill-tool-both");

            tool.SkillBody.ShouldBe(SkillAnnotatedToolClass.BothBody);
        }

        [Fact]
        public async Task ListTool_CopiesSkillDescription_IndependentlyOfSkillBody()
        {
            // Independent halves: a tool that declares only ONE of the two attributes must
            // carry exactly that one. Asserting the exact value (not "not null") so a copy
            // wired to the wrong accessor cannot satisfy it.
            var tool = await GetTool("skill-tool-description-only");

            tool.SkillDescription.ShouldBe(SkillAnnotatedToolClass.DescriptionOnlyDescription);
            tool.SkillBody.ShouldBeNull();
        }

        [Fact]
        public async Task ListTool_CopiesSkillBody_IndependentlyOfSkillDescription()
        {
            var tool = await GetTool("skill-tool-body-only");

            tool.SkillBody.ShouldBe(SkillAnnotatedToolClass.BodyOnlyBody);
            tool.SkillDescription.ShouldBeNull();
        }

        [Fact]
        public async Task ListTool_LeavesSkillMetadataNull_WhenNoAttributes()
        {
            // The "nothing happened" half. Its same-fixture positive control is
            // ListTool_CopiesSkill*_WhenAttributePresent above, which proves the copy path
            // is live for this very builder — so a null here means "no attribute", not
            // "the projection never ran".
            var tool = await GetTool("skill-tool-none");

            tool.Name.ShouldBe("skill-tool-none");   // positive artifact: the tool WAS projected
            tool.Title.ShouldBe("Tool With No Skill Attributes");
            tool.SkillDescription.ShouldBeNull();
            tool.SkillBody.ShouldBeNull();
        }
    }
}
