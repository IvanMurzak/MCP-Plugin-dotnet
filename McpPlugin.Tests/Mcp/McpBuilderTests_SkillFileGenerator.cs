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
using com.IvanMurzak.McpPlugin.Skills;
using com.IvanMurzak.ReflectorNet;
using Shouldly;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    [Collection("McpPlugin")]
    public class McpBuilderTests_SkillFileGenerator
    {
        private readonly Version _version = new Version();
        private readonly Reflector _reflector = new Reflector();

        // â”€â”€ WithSkillFileGenerator<T>() â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Fact]
        public void WithSkillFileGenerator_Generic_FirstCall_DoesNotThrow()
        {
            var builder = new McpPluginBuilder(_version);

            Action act = () => builder.WithSkillFileGenerator<CustomSkillFileGenerator>();

            Should.NotThrow(act);
        }

        [Fact]
        public void WithSkillFileGenerator_Generic_CalledTwice_ThrowsInvalidOperationException()
        {
            var builder = new McpPluginBuilder(_version);
            builder.WithSkillFileGenerator<CustomSkillFileGenerator>();

            Action act = () => builder.WithSkillFileGenerator<CustomSkillFileGenerator>();

            var ex = Should.Throw<InvalidOperationException>(act);
            ex.Message.ShouldContain(nameof(ISkillFileGenerator));
            ex.Message.ShouldContain("already been set");
        }

        // â”€â”€ WithSkillFileGenerator(instance) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Fact]
        public void WithSkillFileGenerator_Instance_FirstCall_DoesNotThrow()
        {
            var builder = new McpPluginBuilder(_version);
            var instance = new CustomSkillFileGenerator();

            Action act = () => builder.WithSkillFileGenerator(instance);

            Should.NotThrow(act);
        }

        [Fact]
        public void WithSkillFileGenerator_Instance_CalledTwice_ThrowsInvalidOperationException()
        {
            var builder = new McpPluginBuilder(_version);
            builder.WithSkillFileGenerator(new CustomSkillFileGenerator());

            Action act = () => builder.WithSkillFileGenerator(new CustomSkillFileGenerator());

            var ex = Should.Throw<InvalidOperationException>(act);
            ex.Message.ShouldContain(nameof(ISkillFileGenerator));
            ex.Message.ShouldContain("already been set");
        }

        [Fact]
        public void WithSkillFileGenerator_Instance_NullArgument_ThrowsArgumentNullException()
        {
            var builder = new McpPluginBuilder(_version);

            Action act = () => builder.WithSkillFileGenerator(null!);

            Should.Throw<ArgumentNullException>(act);
        }

        // â”€â”€ cross-overload duplicate detection â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Fact]
        public void WithSkillFileGenerator_Generic_ThenInstance_ThrowsInvalidOperationException()
        {
            var builder = new McpPluginBuilder(_version);
            builder.WithSkillFileGenerator<CustomSkillFileGenerator>();

            Action act = () => builder.WithSkillFileGenerator(new CustomSkillFileGenerator());

            var ex = Should.Throw<InvalidOperationException>(act);
            ex.Message.ShouldContain(nameof(ISkillFileGenerator));
            ex.Message.ShouldContain("already been set");
        }

        [Fact]
        public void WithSkillFileGenerator_Instance_ThenGeneric_ThrowsInvalidOperationException()
        {
            var builder = new McpPluginBuilder(_version);
            builder.WithSkillFileGenerator(new CustomSkillFileGenerator());

            Action act = () => builder.WithSkillFileGenerator<CustomSkillFileGenerator>();

            var ex = Should.Throw<InvalidOperationException>(act);
            ex.Message.ShouldContain(nameof(ISkillFileGenerator));
            ex.Message.ShouldContain("already been set");
        }

        // â”€â”€ after-build guard â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Fact]
        public void WithSkillFileGenerator_Generic_AfterBuild_ThrowsInvalidOperationException()
        {
            var builder = new McpPluginBuilder(_version);
            builder.Build(_reflector);

            Action act = () => builder.WithSkillFileGenerator<CustomSkillFileGenerator>();

            Should.Throw<InvalidOperationException>(act)
                .Message.ShouldBe("The builder has already been built.");
        }

        [Fact]
        public void WithSkillFileGenerator_Instance_AfterBuild_ThrowsInvalidOperationException()
        {
            var builder = new McpPluginBuilder(_version);
            builder.Build(_reflector);

            Action act = () => builder.WithSkillFileGenerator(new CustomSkillFileGenerator());

            Should.Throw<InvalidOperationException>(act)
                .Message.ShouldBe("The builder has already been built.");
        }

        // â”€â”€ DI resolution â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Fact]
        public void WithSkillFileGenerator_Generic_Build_ResolvesCustomType()
        {
            var builder = new McpPluginBuilder(_version);
            builder.WithSkillFileGenerator<CustomSkillFileGenerator>();
            builder.Build(_reflector);

            var resolved = builder.ServiceProvider!.GetRequiredService<ISkillFileGenerator>();

            resolved.ShouldBeOfType<CustomSkillFileGenerator>();
        }

        [Fact]
        public void WithSkillFileGenerator_Instance_Build_ResolvesProvidedInstance()
        {
            var builder = new McpPluginBuilder(_version);
            var instance = new CustomSkillFileGenerator();
            builder.WithSkillFileGenerator(instance);
            builder.Build(_reflector);

            var resolved = builder.ServiceProvider!.GetRequiredService<ISkillFileGenerator>();

            resolved.ShouldBeSameAs(instance);
        }

        [Fact]
        public void WithoutWithSkillFileGenerator_Build_ResolvesDefaultType()
        {
            var builder = new McpPluginBuilder(_version);
            builder.Build(_reflector);

            var resolved = builder.ServiceProvider!.GetRequiredService<ISkillFileGenerator>();

            resolved.ShouldBeOfType<SkillFileGenerator>();
        }

        // â”€â”€ helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private sealed class CustomSkillFileGenerator : SkillFileGenerator { }
    }
}
