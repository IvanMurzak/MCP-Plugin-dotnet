using System;
using System.Collections.Generic;
using com.IvanMurzak.ReflectorNet;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    public class McpResourceManagerTests : IDisposable
    {
        private readonly Mock<ILogger<McpResourceManager>> _mockLogger;
        private readonly Mock<Reflector> _mockReflector;
        private readonly ResourceRunnerCollection _resourceCollection;
        private readonly McpResourceManager _manager;

        public McpResourceManagerTests()
        {
            // Setup dependencies
            _mockLogger = new Mock<ILogger<McpResourceManager>>();
            _mockReflector = new Mock<Reflector>();
            _resourceCollection = new ResourceRunnerCollection(_mockReflector.Object, null);

            // Create the manager with proper dependencies
            _manager = new McpResourceManager(
                _mockLogger.Object,
                _mockReflector.Object,
                _resourceCollection
            );
        }

        public void Dispose()
        {
            // Clean up resources after each test
            _manager.Dispose();
            GC.SuppressFinalize(this);
        }

        [Fact]
        public void IsMatch_ReturnsTrue_ForMatchingTemplate()
        {
            var result = _manager.IsMatch("/files/{id}/content", "/files/123/content");

            result.Should().BeTrue();
        }

        [Fact]
        public void IsMatch_ReturnsFalse_ForNonMatchingTemplate()
        {
            var result = _manager.IsMatch("/files/{id}/content", "/files/123/other");

            result.Should().BeFalse();
        }

        [Fact]
        public void ParseUriParameters_ParsesNamedParameter_IncludingTrailingSegments()
        {
            var dict = _manager.ParseUriParameters("/files/{id}", "/files/123/other");

            dict.Should().ContainKey("uri").WhoseValue.Should().Be("/files/123/other");
            dict.Should().ContainKey("id");
            dict["id"].Should().Be("123/other");
        }

        [Fact]
        public void ParseUriParameters_ParsesMultipleParameters()
        {
            var dict = _manager.ParseUriParameters("/{a}/{b}", "/one/two");

            dict["a"].Should().Be("one");
            dict["b"].Should().Be("two");
            dict["uri"].Should().Be("/one/two");
        }

        [Fact]
        public void GameObjectTemplate_ParsesPathParameter()
        {
            var template = "gameObject://currentScene/{path}";
            var uri = "gameObject://currentScene/Player/Armature/Hand";

            var isMatch = _manager.IsMatch(template, uri);
            isMatch.Should().BeTrue();

            var dict = _manager.ParseUriParameters(template, uri);
            dict.Should().ContainKey("path");
            dict["path"].Should().Be("Player/Armature/Hand");
            dict["uri"].Should().Be(uri);
        }

        [Fact]
        public void FindResourceContentRunner_ReturnsMatchingResource()
        {
            var mockResource = new Mock<IRunResource>();
            mockResource.Setup(r => r.Route).Returns("/files/{id}");
            mockResource.Setup(r => r.Name).Returns("files-resource");

            var resources = new Dictionary<string, IRunResource>
            {
                { "files-resource", mockResource.Object }
            };

            var result = _manager.FindResourceContentRunner("/files/123", resources, out var uriTemplate);

            result.Should().NotBeNull();
            result.Should().BeSameAs(mockResource.Object);
            uriTemplate.Should().Be("/files/{id}");
        }

        [Fact]
        public void FindResourceContentRunner_ReturnsNull_WhenNoMatch()
        {
            var mockResource = new Mock<IRunResource>();
            mockResource.Setup(r => r.Route).Returns("/files/{id}");
            mockResource.Setup(r => r.Name).Returns("files-resource");

            var resources = new Dictionary<string, IRunResource>
            {
                { "files-resource", mockResource.Object }
            };

            var result = _manager.FindResourceContentRunner("/users/123", resources, out var uriTemplate);

            result.Should().BeNull();
            uriTemplate.Should().BeNull();
        }

        [Fact]
        public void FindResourceContentRunner_ReturnsNull_WhenResourcesEmpty()
        {
            var resources = new Dictionary<string, IRunResource>();

            var result = _manager.FindResourceContentRunner("/files/123", resources, out var uriTemplate);

            result.Should().BeNull();
            uriTemplate.Should().BeNull();
        }

        [Fact]
        public void FindResourceContentRunner_ReturnsFirstMatchingResource_WhenMultipleExist()
        {
            var mockResource1 = new Mock<IRunResource>();
            mockResource1.Setup(r => r.Route).Returns("/files/{id}");
            mockResource1.Setup(r => r.Name).Returns("files-resource");

            var mockResource2 = new Mock<IRunResource>();
            mockResource2.Setup(r => r.Route).Returns("/users/{id}");
            mockResource2.Setup(r => r.Name).Returns("users-resource");

            var resources = new Dictionary<string, IRunResource>
            {
                { "files-resource", mockResource1.Object },
                { "users-resource", mockResource2.Object }
            };

            var result = _manager.FindResourceContentRunner("/users/456", resources, out var uriTemplate);

            result.Should().NotBeNull();
            result.Should().BeSameAs(mockResource2.Object);
            uriTemplate.Should().Be("/users/{id}");
        }

        [Fact]
        public void FindResourceContentRunner_MatchesWithTrailingSegments()
        {
            var mockResource = new Mock<IRunResource>();
            mockResource.Setup(r => r.Route).Returns("gameObject://currentScene/{path}");
            mockResource.Setup(r => r.Name).Returns("gameobject-resource");

            var resources = new Dictionary<string, IRunResource>
            {
                { "gameobject-resource", mockResource.Object }
            };

            var result = _manager.FindResourceContentRunner(
                "gameObject://currentScene/Player/Armature/Hand",
                resources,
                out var uriTemplate);

            result.Should().NotBeNull();
            result.Should().BeSameAs(mockResource.Object);
            uriTemplate.Should().Be("gameObject://currentScene/{path}");
        }
    }
}
