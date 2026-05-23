/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Linq;
using System.Reflection;
using com.IvanMurzak.McpPlugin.Skills;
using com.IvanMurzak.McpPlugin.Tests.Data.Annotations;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using Xunit.Abstractions;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    /// <summary>
    /// Verifies that the rename of <c>McpPlugin*Attribute</c> → <c>Ai*Attribute</c> preserves
    /// reflection-discovery semantics: code that looks up the NEW canonical attribute type
    /// (<see cref="AiToolAttribute"/> / <see cref="AiPromptAttribute"/> /
    /// <see cref="AiResourceAttribute"/> / <see cref="AiSkillAttribute"/>) MUST also discover
    /// members decorated with the LEGACY <c>[McpPlugin*]</c> attributes, because each old
    /// class is now an <see cref="System.ObsoleteAttribute"/>-marked subclass of its new
    /// canonical counterpart.
    ///
    /// The fixture data this test reads from
    /// (<see cref="MixedAliasToolClass"/>, <see cref="MixedAliasPromptClass"/>,
    /// <see cref="MixedAliasResourceClass"/>, <see cref="MixedAliasSkillClass"/>) intentionally
    /// decorates one member with the new <c>[Ai*]</c> attribute and another with the legacy
    /// <c>[McpPlugin*]</c> attribute, side by side.
    /// </summary>
    [Collection("McpPlugin")]
    public class AttributeAliasDiscoveryTests
    {
        private readonly ITestOutputHelper _output;
        private readonly XunitTestOutputLoggerProvider _loggerProvider;
        private readonly Version _version = new Version();

        public AttributeAliasDiscoveryTests(ITestOutputHelper output)
        {
            _output = output;
            _loggerProvider = new XunitTestOutputLoggerProvider(output);
        }

        // ── Direct reflection lookup: the canonical [Ai*] attribute type
        //    discovers BOTH old-style and new-style decorations via inheritance.

        [Fact]
        public void GetCustomAttribute_AiTool_FindsLegacyMcpPluginToolDecoration()
        {
            var newMethod = typeof(MixedAliasToolClass).GetMethod(nameof(MixedAliasToolClass.NewStyle), BindingFlags.Public | BindingFlags.Static);
            var oldMethod = typeof(MixedAliasToolClass).GetMethod(nameof(MixedAliasToolClass.OldStyle), BindingFlags.Public | BindingFlags.Static);

            newMethod.ShouldNotBeNull();
            oldMethod.ShouldNotBeNull();

            // Looking up by the NEW canonical type must find BOTH decorations.
            var newAttr = newMethod!.GetCustomAttribute<AiToolAttribute>();
            var oldAttr = oldMethod!.GetCustomAttribute<AiToolAttribute>();

            newAttr.ShouldNotBeNull();
            newAttr!.Name.ShouldBe("alias-tool-new");

            oldAttr.ShouldNotBeNull();
            oldAttr!.Name.ShouldBe("alias-tool-old");
        }

        [Fact]
        public void GetCustomAttribute_AiPrompt_FindsLegacyMcpPluginPromptDecoration()
        {
            var newMethod = typeof(MixedAliasPromptClass).GetMethod(nameof(MixedAliasPromptClass.NewStyle), BindingFlags.Public | BindingFlags.Static);
            var oldMethod = typeof(MixedAliasPromptClass).GetMethod(nameof(MixedAliasPromptClass.OldStyle), BindingFlags.Public | BindingFlags.Static);

            newMethod.ShouldNotBeNull();
            oldMethod.ShouldNotBeNull();

            var newAttr = newMethod!.GetCustomAttribute<AiPromptAttribute>();
            var oldAttr = oldMethod!.GetCustomAttribute<AiPromptAttribute>();

            newAttr.ShouldNotBeNull();
            newAttr!.Name.ShouldBe("alias-prompt-new");

            oldAttr.ShouldNotBeNull();
            oldAttr!.Name.ShouldBe("alias-prompt-old");
        }

        [Fact]
        public void GetCustomAttribute_AiResource_FindsLegacyMcpPluginResourceDecoration()
        {
            var newMethod = typeof(MixedAliasResourceClass).GetMethod(nameof(MixedAliasResourceClass.GetNew), BindingFlags.Public | BindingFlags.Static);
            var oldMethod = typeof(MixedAliasResourceClass).GetMethod(nameof(MixedAliasResourceClass.GetOld), BindingFlags.Public | BindingFlags.Static);

            newMethod.ShouldNotBeNull();
            oldMethod.ShouldNotBeNull();

            var newAttr = newMethod!.GetCustomAttribute<AiResourceAttribute>();
            var oldAttr = oldMethod!.GetCustomAttribute<AiResourceAttribute>();

            newAttr.ShouldNotBeNull();
            newAttr!.Name.ShouldBe("alias-resource-new");

            oldAttr.ShouldNotBeNull();
            oldAttr!.Name.ShouldBe("alias-resource-old");
        }

        [Fact]
        public void GetCustomAttribute_AiSkill_FindsLegacyMcpPluginSkillDecoration()
        {
            var newField = typeof(MixedAliasSkillClass).GetField(nameof(MixedAliasSkillClass.NewStyle), BindingFlags.Public | BindingFlags.Static);
            var oldField = typeof(MixedAliasSkillClass).GetField(nameof(MixedAliasSkillClass.OldStyle), BindingFlags.Public | BindingFlags.Static);

            newField.ShouldNotBeNull();
            oldField.ShouldNotBeNull();

            var newAttr = newField!.GetCustomAttribute<AiSkillAttribute>();
            var oldAttr = oldField!.GetCustomAttribute<AiSkillAttribute>();

            newAttr.ShouldNotBeNull();
            newAttr!.Name.ShouldBe("alias-skill-new");

            oldAttr.ShouldNotBeNull();
            oldAttr!.Name.ShouldBe("alias-skill-old");
        }

        // ── End-to-end via the actual McpPluginBuilder scanner code path.

        [Fact]
        public void BuilderScanner_DiscoversBothNewAndLegacyToolDecorations()
        {
            var plugin = new McpPluginBuilder(_version, _loggerProvider)
                .AddLogging(b => b.AddXunitTestOutput(_output))
                .WithTools(typeof(MixedAliasToolClass))
                .Build(new Reflector());

            var tools = plugin.McpManager.ToolManager!.GetAllTools().Select(t => t.Name).ToList();

            tools.ShouldContain("alias-tool-new");
            tools.ShouldContain("alias-tool-old");
        }

        [Fact]
        public void BuilderScanner_DiscoversBothNewAndLegacyPromptDecorations()
        {
            var plugin = new McpPluginBuilder(_version, _loggerProvider)
                .AddLogging(b => b.AddXunitTestOutput(_output))
                .WithPrompts(typeof(MixedAliasPromptClass))
                .Build(new Reflector());

            var prompts = plugin.McpManager.PromptManager!.GetAllPrompts().Select(p => p.Name).ToList();

            prompts.ShouldContain("alias-prompt-new");
            prompts.ShouldContain("alias-prompt-old");
        }

        [Fact]
        public void BuilderScanner_DiscoversBothNewAndLegacyResourceDecorations()
        {
            var plugin = new McpPluginBuilder(_version, _loggerProvider)
                .AddLogging(b => b.AddXunitTestOutput(_output))
                .WithResources(typeof(MixedAliasResourceClass))
                .Build(new Reflector());

            var routes = plugin.McpManager.ResourceManager!.GetAllResources().Select(r => r.Route).ToList();

            routes.ShouldContain("test://alias-resource-new/{id}");
            routes.ShouldContain("test://alias-resource-old/{id}");
        }

        [Fact]
        public void BuilderScanner_DiscoversBothNewAndLegacySkillDecorations()
        {
            var builder = new McpPluginBuilder(_version, _loggerProvider);
            builder
                .AddLogging(b => b.AddXunitTestOutput(_output))
                .WithSkills(typeof(MixedAliasSkillClass));
            builder.Build(new Reflector());

            var collection = builder.ServiceProvider!.GetRequiredService<SkillContentCollection>();
            collection.ContainsKey("alias-skill-new").ShouldBeTrue();
            collection.ContainsKey("alias-skill-old").ShouldBeTrue();
        }
    }
}
