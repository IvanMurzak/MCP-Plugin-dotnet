using com.IvanMurzak.ReflectorNet;
using FluentAssertions;
using Xunit;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Serialization
{
    public class JsonSerializationTests
    {
        private class TestDto
        {
            public string PascalCaseProperty { get; set; } = "Default";
            public int AnotherProperty { get; set; } = 42;
        }

        [Fact]
        public void McpPluginBuilder_ShouldConfigureReflector_ToUsePascalCaseAndCaseInsensitive()
        {
            // Arrange
            var reflector = new Reflector();

            var builder = new McpPluginBuilder(new Version())
                .AddLogging(b => { });

            // Act
            builder.Build(reflector);

            // Assert
            reflector.JsonSerializerOptions.PropertyNamingPolicy.Should().BeNull();
            reflector.JsonSerializerOptions.PropertyNameCaseInsensitive.Should().BeTrue();
        }

        [Fact]
        public void Serialize_ShouldPreservePascalCase_AfterMcpPluginBuild()
        {
            // Arrange
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(new Version())
                .AddLogging(b => { });
            builder.Build(reflector);

            var dto = new TestDto
            {
                PascalCaseProperty = "TestValue",
                AnotherProperty = 123
            };

            // Act
            var json = reflector.JsonSerializer.Serialize(dto);

            // Assert
            json.Should().Contain("\"PascalCaseProperty\": \"TestValue\"");
            json.Should().Contain("\"AnotherProperty\": 123");
        }

        [Theory]
        [InlineData("PascalCaseProperty")]
        [InlineData("pascalCaseProperty")]
        [InlineData("pascalcaseproperty")]
        [InlineData("pascalcaseProperty")]
        [InlineData("pAscalCaseProperty")]
        [InlineData("PASCALCASEPROPERTY")]
        [InlineData("PascalcaseProperty")]
        [InlineData("pascalCaseproperty")]
        public void Deserialize_ShouldHandleVariousCasing_AfterMcpPluginBuild(string jsonPropertyName)
        {
            // Arrange
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(new Version())
                .AddLogging(b => { });
            builder.Build(reflector);

            var json = $"{{\"{jsonPropertyName}\": \"TestValue\"}}";

            // Act
            var result = reflector.JsonSerializer.Deserialize<TestDto>(json);

            // Assert
            result.Should().NotBeNull();
            result!.PascalCaseProperty.Should().Be("TestValue");
        }
    }
}
