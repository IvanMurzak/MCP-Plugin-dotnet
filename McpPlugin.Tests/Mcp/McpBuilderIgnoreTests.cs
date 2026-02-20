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
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Tests.Data.Ignored;
using com.IvanMurzak.McpPlugin.Tests.Data.Ignored.SubNamespace;
using com.IvanMurzak.McpPlugin.Tests.Data.Included;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using com.IvanMurzak.ReflectorNet;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    [Collection("McpPlugin")]
    public class McpBuilderIgnoreTests
    {
        private readonly ITestOutputHelper _output;
        private readonly XunitTestOutputLoggerProvider _loggerProvider;
        private readonly Version _version = new Version();

        public McpBuilderIgnoreTests(ITestOutputHelper output)
        {
            _output = output;
            _loggerProvider = new XunitTestOutputLoggerProvider(output);
        }

        #region Assembly Ignore Tests

        [Fact]
        public async Task IgnoreAssembly_ByInstance_ShouldExcludeToolsFromAssembly()
        {
            // Arrange
            var reflector = new Reflector();
            var testAssembly = typeof(IgnoreTestToolClass).Assembly;
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithToolsFromAssembly(testAssembly)
                .IgnoreAssembly(testAssembly);

            // Act
            var plugin = builder.Build(reflector);
            var response = await plugin.McpManager.ToolManager!.RunListTool(new RequestListTool());

            // Assert
            response.Value.Should().NotBeNull();
            response.Value!.Should().NotContain(t => t.Name == "ignore-test-tool");
            response.Value!.Should().NotContain(t => t.Name == "sub-namespace-tool");
            response.Value!.Should().NotContain(t => t.Name == "include-test-tool");
        }

        [Fact]
        public async Task IgnoreAssembly_ByName_ShouldExcludeToolsFromAssembly()
        {
            // Arrange
            var reflector = new Reflector();
            var testAssembly = typeof(IgnoreTestToolClass).Assembly;
            var assemblyName = testAssembly.GetName().Name!;
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithToolsFromAssembly(testAssembly)
                .IgnoreAssembly(assemblyName);

            // Act
            var plugin = builder.Build(reflector);
            var response = await plugin.McpManager.ToolManager!.RunListTool(new RequestListTool());

            // Assert
            response.Value.Should().NotBeNull();
            response.Value!.Should().NotContain(t => t.Name == "ignore-test-tool");
        }

        [Fact]
        public void IgnoreAssembly_AfterBuild_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider);
            builder.Build(reflector);

            // Act
            Action act = () => builder.IgnoreAssembly(typeof(IgnoreTestToolClass).Assembly);

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("The builder has already been built.");
        }

        #endregion

        #region Namespace Ignore Tests

        [Fact]
        public async Task IgnoreNamespace_ExactMatch_ShouldExcludeTypesInNamespace()
        {
            // Arrange
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithTools<IgnoreTestToolClass>()
                .WithTools<IncludeTestToolClass>()
                .IgnoreNamespace("com.IvanMurzak.McpPlugin.Tests.Data.Ignored");

            // Act
            var plugin = builder.Build(reflector);
            var response = await plugin.McpManager.ToolManager!.RunListTool(new RequestListTool());

            // Assert
            response.Value.Should().NotBeNull();
            response.Value!.Should().NotContain(t => t.Name == "ignore-test-tool");
            response.Value!.Should().Contain(t => t.Name == "include-test-tool");
        }

        [Fact]
        public async Task IgnoreNamespace_ShouldExcludeSubNamespaces()
        {
            // Arrange
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithTools<IgnoreTestToolClass>()
                .WithTools<SubNamespaceToolClass>()
                .WithTools<IncludeTestToolClass>()
                .IgnoreNamespace("com.IvanMurzak.McpPlugin.Tests.Data.Ignored");

            // Act
            var plugin = builder.Build(reflector);
            var response = await plugin.McpManager.ToolManager!.RunListTool(new RequestListTool());

            // Assert
            response.Value.Should().NotBeNull();
            response.Value!.Should().NotContain(t => t.Name == "ignore-test-tool");
            response.Value!.Should().NotContain(t => t.Name == "sub-namespace-tool");
            response.Value!.Should().Contain(t => t.Name == "include-test-tool");
        }

        [Fact]
        public async Task IgnoreNamespaces_Multiple_ShouldExcludeAll()
        {
            // Arrange
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithTools<IgnoreTestToolClass>()
                .WithTools<IncludeTestToolClass>()
                .IgnoreNamespaces(
                    "com.IvanMurzak.McpPlugin.Tests.Data.Ignored",
                    "com.IvanMurzak.McpPlugin.Tests.Data.Included");

            // Act
            var plugin = builder.Build(reflector);
            var response = await plugin.McpManager.ToolManager!.RunListTool(new RequestListTool());

            // Assert
            response.Value.Should().NotBeNull();
            response.Value!.Should().NotContain(t => t.Name == "ignore-test-tool");
            response.Value!.Should().NotContain(t => t.Name == "include-test-tool");
        }

        [Fact]
        public void IgnoreNamespace_AfterBuild_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider);
            builder.Build(reflector);

            // Act
            Action act = () => builder.IgnoreNamespace("com.IvanMurzak.McpPlugin.Tests.Data.Ignored");

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("The builder has already been built.");
        }

        #endregion

        #region Lazy Execution Tests

        [Fact]
        public async Task WithToolsFromAssembly_IgnoreCalledAfter_ShouldStillExclude()
        {
            // Arrange
            var reflector = new Reflector();
            var testAssembly = typeof(IgnoreTestToolClass).Assembly;

            // Call WithToolsFromAssembly BEFORE IgnoreNamespace - should still work due to lazy evaluation
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithToolsFromAssembly(testAssembly)
                .IgnoreNamespace("com.IvanMurzak.McpPlugin.Tests.Data.Ignored");

            // Act
            var plugin = builder.Build(reflector);
            var response = await plugin.McpManager.ToolManager!.RunListTool(new RequestListTool());

            // Assert
            response.Value.Should().NotBeNull();
            response.Value!.Should().NotContain(t => t.Name == "ignore-test-tool");
            response.Value!.Should().NotContain(t => t.Name == "sub-namespace-tool");
            response.Value!.Should().Contain(t => t.Name == "include-test-tool");
        }

        [Fact]
        public async Task WithPromptsFromAssembly_IgnoreCalledAfter_ShouldStillExclude()
        {
            // Arrange
            var reflector = new Reflector();
            var testAssembly = typeof(IgnoreTestPromptClass).Assembly;

            // Call WithPromptsFromAssembly BEFORE IgnoreNamespace - should still work due to lazy evaluation
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithPromptsFromAssembly(testAssembly)
                .IgnoreNamespace("com.IvanMurzak.McpPlugin.Tests.Data.Ignored");

            // Act
            var plugin = builder.Build(reflector);
            var response = await plugin.McpManager.PromptManager!.RunListPrompts(new RequestListPrompts());

            // Assert
            response.Value.Should().NotBeNull();
            response.Value!.Prompts.Should().NotContain(p => p.Name == "ignore-test-prompt");
            response.Value!.Prompts.Should().NotContain(p => p.Name == "sub-namespace-prompt");
            response.Value!.Prompts.Should().Contain(p => p.Name == "include-test-prompt");
        }

        #endregion

        #region Remove Assembly Ignore Tests

        [Fact]
        public async Task RemoveIgnoredAssembly_ByInstance_ShouldIncludeToolsFromAssembly()
        {
            // Arrange
            var reflector = new Reflector();
            var testAssembly = typeof(IgnoreTestToolClass).Assembly;
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithToolsFromAssembly(testAssembly)
                .IgnoreAssembly(testAssembly)
                .RemoveIgnoredAssembly(testAssembly);

            // Act
            var plugin = builder.Build(reflector);
            var response = await plugin.McpManager.ToolManager!.RunListTool(new RequestListTool());

            // Assert
            response.Value.Should().NotBeNull();
            response.Value!.Should().Contain(t => t.Name == "ignore-test-tool");
            response.Value!.Should().Contain(t => t.Name == "include-test-tool");
        }

        [Fact]
        public async Task RemoveIgnoredAssembly_ByName_ShouldIncludeToolsFromAssembly()
        {
            // Arrange
            var reflector = new Reflector();
            var testAssembly = typeof(IgnoreTestToolClass).Assembly;
            var assemblyName = testAssembly.GetName().Name!;
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithToolsFromAssembly(testAssembly)
                .IgnoreAssembly(assemblyName)
                .RemoveIgnoredAssembly(assemblyName);

            // Act
            var plugin = builder.Build(reflector);
            var response = await plugin.McpManager.ToolManager!.RunListTool(new RequestListTool());

            // Assert
            response.Value.Should().NotBeNull();
            response.Value!.Should().Contain(t => t.Name == "ignore-test-tool");
        }

        [Fact]
        public void RemoveIgnoredAssembly_AfterBuild_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider);
            builder.Build(reflector);

            // Act
            Action act = () => builder.RemoveIgnoredAssembly(typeof(IgnoreTestToolClass).Assembly);

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("The builder has already been built.");
        }

        #endregion

        #region Remove Namespace Ignore Tests

        [Fact]
        public async Task RemoveIgnoredNamespace_ShouldIncludeTypesInNamespace()
        {
            // Arrange
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithTools<IgnoreTestToolClass>()
                .WithTools<IncludeTestToolClass>()
                .IgnoreNamespace("com.IvanMurzak.McpPlugin.Tests.Data.Ignored")
                .RemoveIgnoredNamespace("com.IvanMurzak.McpPlugin.Tests.Data.Ignored");

            // Act
            var plugin = builder.Build(reflector);
            var response = await plugin.McpManager.ToolManager!.RunListTool(new RequestListTool());

            // Assert
            response.Value.Should().NotBeNull();
            response.Value!.Should().Contain(t => t.Name == "ignore-test-tool");
            response.Value!.Should().Contain(t => t.Name == "include-test-tool");
        }

        [Fact]
        public async Task RemoveIgnoredNamespaces_Multiple_ShouldIncludeAll()
        {
            // Arrange
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithTools<IgnoreTestToolClass>()
                .WithTools<IncludeTestToolClass>()
                .IgnoreNamespaces(
                    "com.IvanMurzak.McpPlugin.Tests.Data.Ignored",
                    "com.IvanMurzak.McpPlugin.Tests.Data.Included")
                .RemoveIgnoredNamespaces(
                    "com.IvanMurzak.McpPlugin.Tests.Data.Ignored",
                    "com.IvanMurzak.McpPlugin.Tests.Data.Included");

            // Act
            var plugin = builder.Build(reflector);
            var response = await plugin.McpManager.ToolManager!.RunListTool(new RequestListTool());

            // Assert
            response.Value.Should().NotBeNull();
            response.Value!.Should().Contain(t => t.Name == "ignore-test-tool");
            response.Value!.Should().Contain(t => t.Name == "include-test-tool");
        }

        [Fact]
        public void RemoveIgnoredNamespace_AfterBuild_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider);
            builder.Build(reflector);

            // Act
            Action act = () => builder.RemoveIgnoredNamespace("com.IvanMurzak.McpPlugin.Tests.Data.Ignored");

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("The builder has already been built.");
        }

        #endregion

        #region Clear Ignore Tests

        [Fact]
        public async Task ClearIgnoredAssemblies_ShouldIncludeAllAssemblyTools()
        {
            // Arrange
            var reflector = new Reflector();
            var testAssembly = typeof(IgnoreTestToolClass).Assembly;
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithToolsFromAssembly(testAssembly)
                .IgnoreAssembly(testAssembly)
                .ClearIgnoredAssemblies();

            // Act
            var plugin = builder.Build(reflector);
            var response = await plugin.McpManager.ToolManager!.RunListTool(new RequestListTool());

            // Assert
            response.Value.Should().NotBeNull();
            response.Value!.Should().Contain(t => t.Name == "ignore-test-tool");
            response.Value!.Should().Contain(t => t.Name == "include-test-tool");
        }

        [Fact]
        public async Task ClearIgnoredAssemblies_ShouldClearBothInstancesAndNames()
        {
            // Arrange
            var reflector = new Reflector();
            var testAssembly = typeof(IgnoreTestToolClass).Assembly;
            var assemblyName = testAssembly.GetName().Name!;
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithToolsFromAssembly(testAssembly)
                .IgnoreAssembly(testAssembly)
                .IgnoreAssembly(assemblyName)
                .ClearIgnoredAssemblies();

            // Act
            var plugin = builder.Build(reflector);
            var response = await plugin.McpManager.ToolManager!.RunListTool(new RequestListTool());

            // Assert
            response.Value.Should().NotBeNull();
            response.Value!.Should().Contain(t => t.Name == "ignore-test-tool");
        }

        [Fact]
        public async Task ClearIgnoredNamespaces_ShouldIncludeAllNamespaceTools()
        {
            // Arrange
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithTools<IgnoreTestToolClass>()
                .WithTools<SubNamespaceToolClass>()
                .WithTools<IncludeTestToolClass>()
                .IgnoreNamespace("com.IvanMurzak.McpPlugin.Tests.Data.Ignored")
                .ClearIgnoredNamespaces();

            // Act
            var plugin = builder.Build(reflector);
            var response = await plugin.McpManager.ToolManager!.RunListTool(new RequestListTool());

            // Assert
            response.Value.Should().NotBeNull();
            response.Value!.Should().Contain(t => t.Name == "ignore-test-tool");
            response.Value!.Should().Contain(t => t.Name == "sub-namespace-tool");
            response.Value!.Should().Contain(t => t.Name == "include-test-tool");
        }

        [Fact]
        public async Task ClearAllIgnored_ShouldIncludeEverything()
        {
            // Arrange
            var reflector = new Reflector();
            var testAssembly = typeof(IgnoreTestToolClass).Assembly;
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithToolsFromAssembly(testAssembly)
                .IgnoreAssembly(testAssembly)
                .IgnoreNamespace("com.IvanMurzak.McpPlugin.Tests.Data.Ignored")
                .ClearAllIgnored();

            // Act
            var plugin = builder.Build(reflector);
            var response = await plugin.McpManager.ToolManager!.RunListTool(new RequestListTool());

            // Assert
            response.Value.Should().NotBeNull();
            response.Value!.Should().Contain(t => t.Name == "ignore-test-tool");
            response.Value!.Should().Contain(t => t.Name == "sub-namespace-tool");
            response.Value!.Should().Contain(t => t.Name == "include-test-tool");
        }

        [Fact]
        public void ClearIgnoredAssemblies_AfterBuild_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider);
            builder.Build(reflector);

            // Act
            Action act = () => builder.ClearIgnoredAssemblies();

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("The builder has already been built.");
        }

        [Fact]
        public void ClearIgnoredNamespaces_AfterBuild_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider);
            builder.Build(reflector);

            // Act
            Action act = () => builder.ClearIgnoredNamespaces();

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("The builder has already been built.");
        }

        [Fact]
        public void ClearAllIgnored_AfterBuild_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider);
            builder.Build(reflector);

            // Act
            Action act = () => builder.ClearAllIgnored();

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("The builder has already been built.");
        }

        #endregion

        #region Cache Invalidation Tests

        [Fact]
        public void GetIgnoredAssembliesCount_AfterIgnoreAssembly_ShouldInvalidateCache()
        {
            // Arrange
            var testAssembly = typeof(IgnoreTestToolClass).Assembly;
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithToolsFromAssembly(testAssembly);

            // Act - Query count first (populates cache)
            var countBefore = builder.GetIgnoredAssembliesCount();

            // Add ignore after cache is populated
            builder.IgnoreAssembly(testAssembly);
            var countAfter = builder.GetIgnoredAssembliesCount();

            // Assert
            countBefore.Should().Be(0);
            countAfter.Should().Be(1);
        }

        [Fact]
        public void GetIgnoredAssembliesCount_AfterIgnoreAssemblyByName_ShouldInvalidateCache()
        {
            // Arrange
            var testAssembly = typeof(IgnoreTestToolClass).Assembly;
            var assemblyName = testAssembly.GetName().Name!;
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithToolsFromAssembly(testAssembly);

            // Act - Query count first (populates cache)
            var countBefore = builder.GetIgnoredAssembliesCount();

            // Add ignore after cache is populated
            builder.IgnoreAssembly(assemblyName);
            var countAfter = builder.GetIgnoredAssembliesCount();

            // Assert
            countBefore.Should().Be(0);
            countAfter.Should().Be(1);
        }

        [Fact]
        public void GetIgnoredAssembliesCount_AfterRemoveIgnoredAssembly_ShouldInvalidateCache()
        {
            // Arrange
            var testAssembly = typeof(IgnoreTestToolClass).Assembly;
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithToolsFromAssembly(testAssembly)
                .IgnoreAssembly(testAssembly);

            // Act - Query count first (populates cache with ignored state)
            var countBefore = builder.GetIgnoredAssembliesCount();

            // Remove ignore after cache is populated
            builder.RemoveIgnoredAssembly(testAssembly);
            var countAfter = builder.GetIgnoredAssembliesCount();

            // Assert
            countBefore.Should().Be(1);
            countAfter.Should().Be(0);
        }

        [Fact]
        public void GetIgnoredAssembliesCount_AfterRemoveIgnoredAssemblyByName_ShouldInvalidateCache()
        {
            // Arrange
            var testAssembly = typeof(IgnoreTestToolClass).Assembly;
            var assemblyName = testAssembly.GetName().Name!;
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithToolsFromAssembly(testAssembly)
                .IgnoreAssembly(assemblyName);

            // Act - Query count first (populates cache with ignored state)
            var countBefore = builder.GetIgnoredAssembliesCount();

            // Remove ignore after cache is populated
            builder.RemoveIgnoredAssembly(assemblyName);
            var countAfter = builder.GetIgnoredAssembliesCount();

            // Assert
            countBefore.Should().Be(1);
            countAfter.Should().Be(0);
        }

        [Fact]
        public void GetIgnoredAssembliesCount_AfterClearIgnoredAssemblies_ShouldInvalidateCache()
        {
            // Arrange
            var testAssembly = typeof(IgnoreTestToolClass).Assembly;
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithToolsFromAssembly(testAssembly)
                .IgnoreAssembly(testAssembly);

            // Act - Query count first (populates cache with ignored state)
            var countBefore = builder.GetIgnoredAssembliesCount();

            // Clear all ignored assemblies after cache is populated
            builder.ClearIgnoredAssemblies();
            var countAfter = builder.GetIgnoredAssembliesCount();

            // Assert
            countBefore.Should().Be(1);
            countAfter.Should().Be(0);
        }

        [Fact]
        public void GetIgnoredTypesCount_AfterIgnoreNamespace_ShouldInvalidateCache()
        {
            // Arrange - Use WithTools<T> to register types directly (not via assembly scanning)
            // because GetIgnoredTypesCount uses GetExportedTypes which only returns public types
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithTools<IgnoreTestToolClass>()
                .WithTools<IncludeTestToolClass>();

            // Act - Query count first (populates cache)
            var countBefore = builder.GetIgnoredTypesCount();

            // Add namespace ignore after cache is populated
            builder.IgnoreNamespace("com.IvanMurzak.McpPlugin.Tests.Data.Ignored");
            var countAfter = builder.GetIgnoredTypesCount();

            // Assert
            countBefore.Should().Be(0);
            countAfter.Should().Be(1); // IgnoreTestToolClass is in Ignored namespace
        }

        [Fact]
        public void GetIgnoredTypesCount_AfterIgnoreNamespaces_ShouldInvalidateCache()
        {
            // Arrange - Use WithTools<T> to register types directly
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithTools<IgnoreTestToolClass>()
                .WithTools<IncludeTestToolClass>();

            // Act - Query count first (populates cache)
            var countBefore = builder.GetIgnoredTypesCount();

            // Add namespace ignores after cache is populated
            builder.IgnoreNamespaces(
                "com.IvanMurzak.McpPlugin.Tests.Data.Ignored",
                "com.IvanMurzak.McpPlugin.Tests.Data.Included");
            var countAfter = builder.GetIgnoredTypesCount();

            // Assert
            countBefore.Should().Be(0);
            countAfter.Should().Be(2); // Both types are in ignored namespaces
        }

        [Fact]
        public void GetIgnoredTypesCount_AfterRemoveIgnoredNamespace_ShouldInvalidateCache()
        {
            // Arrange - Use WithTools<T> to register types directly
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithTools<IgnoreTestToolClass>()
                .WithTools<IncludeTestToolClass>()
                .IgnoreNamespace("com.IvanMurzak.McpPlugin.Tests.Data.Ignored");

            // Act - Query count first (populates cache with ignored state)
            var countBefore = builder.GetIgnoredTypesCount();

            // Remove namespace ignore after cache is populated
            builder.RemoveIgnoredNamespace("com.IvanMurzak.McpPlugin.Tests.Data.Ignored");
            var countAfter = builder.GetIgnoredTypesCount();

            // Assert
            countBefore.Should().Be(1);
            countAfter.Should().Be(0);
        }

        [Fact]
        public void GetIgnoredTypesCount_AfterRemoveIgnoredNamespaces_ShouldInvalidateCache()
        {
            // Arrange - Use WithTools<T> to register types directly
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithTools<IgnoreTestToolClass>()
                .WithTools<IncludeTestToolClass>()
                .IgnoreNamespaces(
                    "com.IvanMurzak.McpPlugin.Tests.Data.Ignored",
                    "com.IvanMurzak.McpPlugin.Tests.Data.Included");

            // Act - Query count first (populates cache with ignored state)
            var countBefore = builder.GetIgnoredTypesCount();

            // Remove namespace ignores after cache is populated
            builder.RemoveIgnoredNamespaces(
                "com.IvanMurzak.McpPlugin.Tests.Data.Ignored",
                "com.IvanMurzak.McpPlugin.Tests.Data.Included");
            var countAfter = builder.GetIgnoredTypesCount();

            // Assert
            countBefore.Should().Be(2);
            countAfter.Should().Be(0);
        }

        [Fact]
        public void GetIgnoredTypesCount_AfterClearIgnoredNamespaces_ShouldInvalidateCache()
        {
            // Arrange - Use WithTools<T> to register types directly
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithTools<IgnoreTestToolClass>()
                .WithTools<IncludeTestToolClass>()
                .IgnoreNamespace("com.IvanMurzak.McpPlugin.Tests.Data.Ignored");

            // Act - Query count first (populates cache with ignored state)
            var countBefore = builder.GetIgnoredTypesCount();

            // Clear all ignored namespaces after cache is populated
            builder.ClearIgnoredNamespaces();
            var countAfter = builder.GetIgnoredTypesCount();

            // Assert
            countBefore.Should().Be(1);
            countAfter.Should().Be(0);
        }

        [Fact]
        public void GetIgnoredCounts_AfterClearAllIgnored_ShouldInvalidateBothCaches()
        {
            // Arrange
            var testAssembly = typeof(IgnoreTestToolClass).Assembly;
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithToolsFromAssembly(testAssembly)
                .IgnoreAssembly(testAssembly)
                .IgnoreNamespace("com.IvanMurzak.McpPlugin.Tests.Data.Ignored");

            // Act - Query counts first (populates both caches with ignored state)
            var assemblyCountBefore = builder.GetIgnoredAssembliesCount();
            builder.GetIgnoredTypesCount();

            // Clear all ignores after caches are populated
            builder.ClearAllIgnored();
            var assemblyCountAfter = builder.GetIgnoredAssembliesCount();
            var typeCountAfter = builder.GetIgnoredTypesCount();

            // Assert
            assemblyCountBefore.Should().Be(1);
            assemblyCountAfter.Should().Be(0);
            // Note: typeCountBefore may be 0 since the assembly itself was ignored
            typeCountAfter.Should().Be(0);
        }

        [Fact]
        public void GetIgnoredAssembliesCount_AfterIgnoreAssemblies_ShouldInvalidateCache()
        {
            // Arrange
            var testAssembly = typeof(IgnoreTestToolClass).Assembly;
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithToolsFromAssembly(testAssembly);

            // Act - Query count first (populates cache)
            var countBefore = builder.GetIgnoredAssembliesCount();

            // Add multiple ignores after cache is populated
            builder.IgnoreAssemblies(new[] { testAssembly });
            var countAfter = builder.GetIgnoredAssembliesCount();

            // Assert
            countBefore.Should().Be(0);
            countAfter.Should().Be(1);
        }

        [Fact]
        public void GetIgnoredAssembliesCount_AfterIgnoreAssembliesByName_ShouldInvalidateCache()
        {
            // Arrange
            var testAssembly = typeof(IgnoreTestToolClass).Assembly;
            var assemblyName = testAssembly.GetName().Name!;
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithToolsFromAssembly(testAssembly);

            // Act - Query count first (populates cache)
            var countBefore = builder.GetIgnoredAssembliesCount();

            // Add multiple ignores by name after cache is populated
            builder.IgnoreAssemblies(assemblyName);
            var countAfter = builder.GetIgnoredAssembliesCount();

            // Assert
            countBefore.Should().Be(0);
            countAfter.Should().Be(1);
        }

        [Fact]
        public void GetIgnoredAssembliesCount_AfterRemoveIgnoredAssemblies_ShouldInvalidateCache()
        {
            // Arrange
            var testAssembly = typeof(IgnoreTestToolClass).Assembly;
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithToolsFromAssembly(testAssembly)
                .IgnoreAssembly(testAssembly);

            // Act - Query count first (populates cache with ignored state)
            var countBefore = builder.GetIgnoredAssembliesCount();

            // Remove multiple ignores after cache is populated
            builder.RemoveIgnoredAssemblies(new[] { testAssembly });
            var countAfter = builder.GetIgnoredAssembliesCount();

            // Assert
            countBefore.Should().Be(1);
            countAfter.Should().Be(0);
        }

        [Fact]
        public void GetIgnoredAssembliesCount_AfterRemoveIgnoredAssembliesByName_ShouldInvalidateCache()
        {
            // Arrange
            var testAssembly = typeof(IgnoreTestToolClass).Assembly;
            var assemblyName = testAssembly.GetName().Name!;
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithToolsFromAssembly(testAssembly)
                .IgnoreAssembly(assemblyName);

            // Act - Query count first (populates cache with ignored state)
            var countBefore = builder.GetIgnoredAssembliesCount();

            // Remove multiple ignores by name after cache is populated
            builder.RemoveIgnoredAssemblies(assemblyName);
            var countAfter = builder.GetIgnoredAssembliesCount();

            // Assert
            countBefore.Should().Be(1);
            countAfter.Should().Be(0);
        }

        [Fact]
        public void CacheInvalidation_MultipleMutations_ShouldAlwaysReflectCurrentState()
        {
            // Arrange
            var testAssembly = typeof(IgnoreTestToolClass).Assembly;
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithToolsFromAssembly(testAssembly);

            // Act & Assert - Multiple mutations should always reflect current state
            builder.GetIgnoredAssembliesCount().Should().Be(0);

            builder.IgnoreAssembly(testAssembly);
            builder.GetIgnoredAssembliesCount().Should().Be(1);

            builder.RemoveIgnoredAssembly(testAssembly);
            builder.GetIgnoredAssembliesCount().Should().Be(0);

            builder.IgnoreAssembly(testAssembly);
            builder.GetIgnoredAssembliesCount().Should().Be(1);

            builder.ClearIgnoredAssemblies();
            builder.GetIgnoredAssembliesCount().Should().Be(0);
        }

        [Fact]
        public void CacheInvalidation_NamespaceMutations_ShouldAlwaysReflectCurrentState()
        {
            // Arrange
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithTools<IgnoreTestToolClass>()
                .WithTools<IncludeTestToolClass>();

            // Act & Assert - Multiple mutations should always reflect current state
            builder.GetIgnoredTypesCount().Should().Be(0);

            builder.IgnoreNamespace("com.IvanMurzak.McpPlugin.Tests.Data.Ignored");
            var countAfterIgnore = builder.GetIgnoredTypesCount();
            countAfterIgnore.Should().BeGreaterThan(0);

            builder.RemoveIgnoredNamespace("com.IvanMurzak.McpPlugin.Tests.Data.Ignored");
            builder.GetIgnoredTypesCount().Should().Be(0);

            builder.IgnoreNamespace("com.IvanMurzak.McpPlugin.Tests.Data.Ignored");
            builder.GetIgnoredTypesCount().Should().Be(countAfterIgnore);

            builder.ClearIgnoredNamespaces();
            builder.GetIgnoredTypesCount().Should().Be(0);
        }

        #endregion

        #region Prompt Specific Tests

        [Fact]
        public async Task IgnoreNamespace_ShouldExcludePrompts()
        {
            // Arrange
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithPrompts<IgnoreTestPromptClass>()
                .WithPrompts<SubNamespacePromptClass>()
                .WithPrompts<IncludeTestPromptClass>()
                .IgnoreNamespace("com.IvanMurzak.McpPlugin.Tests.Data.Ignored");

            // Act
            var plugin = builder.Build(reflector);
            var response = await plugin.McpManager.PromptManager!.RunListPrompts(new RequestListPrompts());

            // Assert
            response.Value.Should().NotBeNull();
            response.Value!.Prompts.Should().NotContain(p => p.Name == "ignore-test-prompt");
            response.Value!.Prompts.Should().NotContain(p => p.Name == "sub-namespace-prompt");
            response.Value!.Prompts.Should().Contain(p => p.Name == "include-test-prompt");
        }

        #endregion
    }
}
