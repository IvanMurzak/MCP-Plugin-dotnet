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
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using com.IvanMurzak.ReflectorNet;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp.Resource
{
    #region Test Data Classes

    /// <summary>
    /// Test class with static method returning ResponseResourceContent[].
    /// </summary>
    public static class StaticResourceContentProvider
    {
        public static ResponseResourceContent[] GetResource()
        {
            return new[]
            {
                ResponseResourceContent.CreateText("test://static", "Static content")
            };
        }

        public static ResponseResourceContent[] GetResourceWithParam(string name)
        {
            return new[]
            {
                ResponseResourceContent.CreateText($"test://static/{name}", $"Content for {name}")
            };
        }

        public static ResponseResourceContent[] GetMultipleResources(int count)
        {
            var result = new ResponseResourceContent[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = ResponseResourceContent.CreateText($"test://static/{i}", $"Content {i}");
            }
            return result;
        }

        public static string GetInvalidReturnType()
        {
            return "This is not ResponseResourceContent[]";
        }
    }

    /// <summary>
    /// Test class with instance method returning ResponseResourceContent[].
    /// </summary>
    public class InstanceResourceContentProvider
    {
        private readonly string _prefix;

        public InstanceResourceContentProvider(string prefix = "instance")
        {
            _prefix = prefix;
        }

        public ResponseResourceContent[] GetResource()
        {
            return new[]
            {
                ResponseResourceContent.CreateText($"test://{_prefix}", $"{_prefix} content")
            };
        }

        public ResponseResourceContent[] GetResourceWithParam(string name)
        {
            return new[]
            {
                ResponseResourceContent.CreateText($"test://{_prefix}/{name}", $"{_prefix} content for {name}")
            };
        }

        public int GetInvalidReturnType()
        {
            return 42;
        }
    }

    /// <summary>
    /// Test class with async methods returning Task<ResponseResourceContent[]>.
    /// </summary>
    public class AsyncResourceContentProvider
    {
        public async Task<ResponseResourceContent[]> GetResourceAsync()
        {
            await Task.Delay(1);
            return new[]
            {
                ResponseResourceContent.CreateText("test://async", "Async content")
            };
        }

        public async Task<ResponseResourceContent[]> GetResourceWithParamAsync(string name, int count)
        {
            await Task.Delay(1);
            var result = new ResponseResourceContent[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = ResponseResourceContent.CreateText($"test://async/{name}/{i}", $"Async content {i}");
            }
            return result;
        }
    }

    /// <summary>
    /// Test class with method that has default parameters.
    /// </summary>
    public class DefaultParamsResourceProvider
    {
        public ResponseResourceContent[] GetResource(string name = "default", int count = 1)
        {
            var result = new ResponseResourceContent[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = ResponseResourceContent.CreateText($"test://default/{name}/{i}", $"Content {i}");
            }
            return result;
        }
    }

    /// <summary>
    /// Simple test class with parameterless constructor for CreateFromClassMethod tests.
    /// </summary>
    public class SimpleResourceContentProvider
    {
        public ResponseResourceContent[] GetResource()
        {
            return new[]
            {
                ResponseResourceContent.CreateText("test://simple", "Simple content")
            };
        }

        public ResponseResourceContent[] GetResourceWithParam(string name)
        {
            return new[]
            {
                ResponseResourceContent.CreateText($"test://simple/{name}", $"Simple content for {name}")
            };
        }
    }

    #endregion

    /// <summary>
    /// Tests for the RunResourceContent class which provides dynamic method execution
    /// for resource content retrieval.
    /// </summary>
    [Collection("McpPlugin")]
    public class RunResourceContentTests
    {
        private readonly ITestOutputHelper _output;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private readonly Reflector _reflector;

        public RunResourceContentTests(ITestOutputHelper output)
        {
            _output = output;
            _loggerFactory = TestLoggerFactory.Create(output, LogLevel.Trace);
            _logger = _loggerFactory.CreateLogger<RunResourceContentTests>();
            _reflector = new Reflector();
        }

        #region Factory Method Tests - CreateFromStaticMethod

        [Fact]
        public void CreateFromStaticMethod_WithValidMethod_ShouldCreateInstance()
        {
            // Arrange
            var methodInfo = typeof(StaticResourceContentProvider).GetMethod(nameof(StaticResourceContentProvider.GetResource))!;

            // Act
            var runner = RunResourceContent.CreateFromStaticMethod(_reflector, _logger, methodInfo);

            // Assert
            runner.Should().NotBeNull();
        }

        [Fact]
        public async Task CreateFromStaticMethod_Run_ShouldReturnValidResult()
        {
            // Arrange
            var methodInfo = typeof(StaticResourceContentProvider).GetMethod(nameof(StaticResourceContentProvider.GetResource))!;
            var runner = RunResourceContent.CreateFromStaticMethod(_reflector, _logger, methodInfo);

            // Act
            var result = await runner.Run();

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].Uri.Should().Be("test://static");
            result[0].Text.Should().Be("Static content");
        }

        [Fact]
        public async Task CreateFromStaticMethod_RunWithParams_ShouldReturnValidResult()
        {
            // Arrange
            var methodInfo = typeof(StaticResourceContentProvider).GetMethod(nameof(StaticResourceContentProvider.GetResourceWithParam))!;
            var runner = RunResourceContent.CreateFromStaticMethod(_reflector, _logger, methodInfo);

            // Act
            var result = await runner.Run("myResource");

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].Uri.Should().Be("test://static/myResource");
            result[0].Text.Should().Be("Content for myResource");
        }

        [Fact]
        public async Task CreateFromStaticMethod_RunMultipleResources_ShouldReturnAllResults()
        {
            // Arrange
            var methodInfo = typeof(StaticResourceContentProvider).GetMethod(nameof(StaticResourceContentProvider.GetMultipleResources))!;
            var runner = RunResourceContent.CreateFromStaticMethod(_reflector, _logger, methodInfo);

            // Act
            var result = await runner.Run(3);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(3);
            result[0].Uri.Should().Be("test://static/0");
            result[1].Uri.Should().Be("test://static/1");
            result[2].Uri.Should().Be("test://static/2");
        }

        #endregion

        #region Factory Method Tests - CreateFromInstanceMethod

        [Fact]
        public void CreateFromInstanceMethod_WithValidMethod_ShouldCreateInstance()
        {
            // Arrange
            var instance = new InstanceResourceContentProvider("test");
            var methodInfo = typeof(InstanceResourceContentProvider).GetMethod(nameof(InstanceResourceContentProvider.GetResource))!;

            // Act
            var runner = RunResourceContent.CreateFromInstanceMethod(_reflector, _logger, instance, methodInfo);

            // Assert
            runner.Should().NotBeNull();
        }

        [Fact]
        public async Task CreateFromInstanceMethod_Run_ShouldUseInstanceState()
        {
            // Arrange
            var instance = new InstanceResourceContentProvider("customPrefix");
            var methodInfo = typeof(InstanceResourceContentProvider).GetMethod(nameof(InstanceResourceContentProvider.GetResource))!;
            var runner = RunResourceContent.CreateFromInstanceMethod(_reflector, _logger, instance, methodInfo);

            // Act
            var result = await runner.Run();

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].Uri.Should().Be("test://customPrefix");
            result[0].Text.Should().Be("customPrefix content");
        }

        [Fact]
        public async Task CreateFromInstanceMethod_RunWithParams_ShouldReturnValidResult()
        {
            // Arrange
            var instance = new InstanceResourceContentProvider("myInstance");
            var methodInfo = typeof(InstanceResourceContentProvider).GetMethod(nameof(InstanceResourceContentProvider.GetResourceWithParam))!;
            var runner = RunResourceContent.CreateFromInstanceMethod(_reflector, _logger, instance, methodInfo);

            // Act
            var result = await runner.Run("resourceName");

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].Uri.Should().Be("test://myInstance/resourceName");
            result[0].Text.Should().Be("myInstance content for resourceName");
        }

        #endregion

        #region Factory Method Tests - CreateFromClassMethod

        [Fact]
        public void CreateFromClassMethod_WithValidMethod_ShouldCreateInstance()
        {
            // Arrange
            var methodInfo = typeof(SimpleResourceContentProvider).GetMethod(nameof(SimpleResourceContentProvider.GetResource))!;

            // Act
            var runner = RunResourceContent.CreateFromClassMethod(_reflector, _logger, typeof(SimpleResourceContentProvider), methodInfo);

            // Assert
            runner.Should().NotBeNull();
        }

        [Fact]
        public async Task CreateFromClassMethod_Run_ShouldCreateInstanceAndExecute()
        {
            // Arrange
            var methodInfo = typeof(SimpleResourceContentProvider).GetMethod(nameof(SimpleResourceContentProvider.GetResource))!;
            var runner = RunResourceContent.CreateFromClassMethod(_reflector, _logger, typeof(SimpleResourceContentProvider), methodInfo);

            // Act
            var result = await runner.Run();

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].Uri.Should().Be("test://simple");
            result[0].Text.Should().Be("Simple content");
        }

        #endregion

        #region Run With Named Parameters Tests

        [Fact]
        public async Task Run_WithNamedParameters_ShouldReturnValidResult()
        {
            // Arrange
            var methodInfo = typeof(StaticResourceContentProvider).GetMethod(nameof(StaticResourceContentProvider.GetResourceWithParam))!;
            var runner = RunResourceContent.CreateFromStaticMethod(_reflector, _logger, methodInfo);
            var namedParams = new Dictionary<string, object?>
            {
                { "name", "namedResource" }
            };

            // Act
            var result = await runner.Run(namedParams);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].Uri.Should().Be("test://static/namedResource");
            result[0].Text.Should().Be("Content for namedResource");
        }

        [Fact]
        public async Task Run_WithNamedParametersNull_ShouldUseDefaultValues()
        {
            // Arrange
            var methodInfo = typeof(DefaultParamsResourceProvider).GetMethod(nameof(DefaultParamsResourceProvider.GetResource))!;
            var runner = RunResourceContent.CreateFromClassMethod(_reflector, _logger, typeof(DefaultParamsResourceProvider), methodInfo);

            // Act
            var result = await runner.Run((IDictionary<string, object?>?)null);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].Uri.Should().Be("test://default/default/0");
        }

        [Fact]
        public async Task Run_WithPartialNamedParameters_ShouldUseDefaultsForMissing()
        {
            // Arrange
            var methodInfo = typeof(DefaultParamsResourceProvider).GetMethod(nameof(DefaultParamsResourceProvider.GetResource))!;
            var runner = RunResourceContent.CreateFromClassMethod(_reflector, _logger, typeof(DefaultParamsResourceProvider), methodInfo);
            var namedParams = new Dictionary<string, object?>
            {
                { "name", "customName" }
            };

            // Act
            var result = await runner.Run(namedParams);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].Uri.Should().Be("test://default/customName/0");
        }

        [Fact]
        public async Task Run_WithAllNamedParameters_ShouldUseAllProvided()
        {
            // Arrange
            var methodInfo = typeof(DefaultParamsResourceProvider).GetMethod(nameof(DefaultParamsResourceProvider.GetResource))!;
            var runner = RunResourceContent.CreateFromClassMethod(_reflector, _logger, typeof(DefaultParamsResourceProvider), methodInfo);
            var namedParams = new Dictionary<string, object?>
            {
                { "name", "custom" },
                { "count", 2 }
            };

            // Act
            var result = await runner.Run(namedParams);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(2);
            result[0].Uri.Should().Be("test://default/custom/0");
            result[1].Uri.Should().Be("test://default/custom/1");
        }

        #endregion

        #region Async Method Tests

        [Fact]
        public async Task Run_AsyncMethod_ShouldReturnValidResult()
        {
            // Arrange
            var methodInfo = typeof(AsyncResourceContentProvider).GetMethod(nameof(AsyncResourceContentProvider.GetResourceAsync))!;
            var runner = RunResourceContent.CreateFromClassMethod(_reflector, _logger, typeof(AsyncResourceContentProvider), methodInfo);

            // Act
            var result = await runner.Run();

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].Uri.Should().Be("test://async");
            result[0].Text.Should().Be("Async content");
        }

        [Fact]
        public async Task Run_AsyncMethodWithParams_ShouldReturnValidResult()
        {
            // Arrange
            var methodInfo = typeof(AsyncResourceContentProvider).GetMethod(nameof(AsyncResourceContentProvider.GetResourceWithParamAsync))!;
            var runner = RunResourceContent.CreateFromClassMethod(_reflector, _logger, typeof(AsyncResourceContentProvider), methodInfo);

            // Act
            var result = await runner.Run("asyncResource", 2);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(2);
            result[0].Uri.Should().Be("test://async/asyncResource/0");
            result[1].Uri.Should().Be("test://async/asyncResource/1");
        }

        #endregion

        #region Invalid Return Type Tests

        [Fact]
        public async Task Run_WithInvalidReturnType_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var methodInfo = typeof(StaticResourceContentProvider).GetMethod(nameof(StaticResourceContentProvider.GetInvalidReturnType))!;
            var runner = RunResourceContent.CreateFromStaticMethod(_reflector, _logger, methodInfo);

            // Act
            Func<Task> act = async () => await runner.Run();

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*did not return a valid ResponseResourceContent[]*");
        }

        [Fact]
        public async Task Run_InstanceWithInvalidReturnType_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var instance = new InstanceResourceContentProvider();
            var methodInfo = typeof(InstanceResourceContentProvider).GetMethod(nameof(InstanceResourceContentProvider.GetInvalidReturnType))!;
            var runner = RunResourceContent.CreateFromInstanceMethod(_reflector, _logger, instance, methodInfo);

            // Act
            Func<Task> act = async () => await runner.Run();

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*did not return a valid ResponseResourceContent[]*");
        }

        [Fact]
        public async Task Run_NamedParams_WithInvalidReturnType_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var methodInfo = typeof(StaticResourceContentProvider).GetMethod(nameof(StaticResourceContentProvider.GetInvalidReturnType))!;
            var runner = RunResourceContent.CreateFromStaticMethod(_reflector, _logger, methodInfo);

            // Act
            Func<Task> act = async () => await runner.Run((IDictionary<string, object?>?)null);

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*did not return a valid ResponseResourceContent[]*");
        }

        #endregion

        #region Constructor Tests

        [Fact]
        public void Constructor_WithStaticMethod_ShouldCreateInstance()
        {
            // Arrange
            var methodInfo = typeof(StaticResourceContentProvider).GetMethod(nameof(StaticResourceContentProvider.GetResource))!;

            // Act
            var runner = new RunResourceContent(_reflector, _logger, methodInfo);

            // Assert
            runner.Should().NotBeNull();
        }

        [Fact]
        public void Constructor_WithInstanceMethod_ShouldCreateInstance()
        {
            // Arrange
            var instance = new InstanceResourceContentProvider();
            var methodInfo = typeof(InstanceResourceContentProvider).GetMethod(nameof(InstanceResourceContentProvider.GetResource))!;

            // Act
            var runner = new RunResourceContent(_reflector, _logger, instance, methodInfo);

            // Assert
            runner.Should().NotBeNull();
        }

        [Fact]
        public void Constructor_WithClassType_ShouldCreateInstance()
        {
            // Arrange
            var methodInfo = typeof(SimpleResourceContentProvider).GetMethod(nameof(SimpleResourceContentProvider.GetResource))!;

            // Act
            var runner = new RunResourceContent(_reflector, _logger, typeof(SimpleResourceContentProvider), methodInfo);

            // Assert
            runner.Should().NotBeNull();
        }

        #endregion

        #region Null Logger Tests

        [Fact]
        public async Task Run_WithNullLogger_ShouldStillWork()
        {
            // Arrange
            var methodInfo = typeof(StaticResourceContentProvider).GetMethod(nameof(StaticResourceContentProvider.GetResource))!;
            var runner = RunResourceContent.CreateFromStaticMethod(_reflector, null, methodInfo);

            // Act
            var result = await runner.Run();

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
        }

        [Fact]
        public async Task Run_NamedParams_WithNullLogger_ShouldStillWork()
        {
            // Arrange
            var methodInfo = typeof(StaticResourceContentProvider).GetMethod(nameof(StaticResourceContentProvider.GetResourceWithParam))!;
            var runner = RunResourceContent.CreateFromStaticMethod(_reflector, null, methodInfo);
            var namedParams = new Dictionary<string, object?>
            {
                { "name", "test" }
            };

            // Act
            var result = await runner.Run(namedParams);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
        }

        #endregion
    }
}
