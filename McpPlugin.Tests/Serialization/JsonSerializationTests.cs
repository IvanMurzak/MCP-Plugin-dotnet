using com.IvanMurzak.ReflectorNet;
using Shouldly;
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

            // Act
            // Dispose the plugin immediately â€” Build() configures the reflector synchronously.
            // Keeping the plugin alive leaves a ConnectionManager running indefinitely,
            // causing xUnit's AsyncTestSyncContext to wait forever (hang).
            using var plugin = new McpPluginBuilder(new Version())
                .AddLogging(b => { })
                .Build(reflector);

            // Assert
            reflector.JsonSerializerOptions.PropertyNamingPolicy.ShouldBeNull();
            reflector.JsonSerializerOptions.PropertyNameCaseInsensitive.ShouldBeTrue();
        }

        [Fact]
        public void Serialize_ShouldPreservePascalCase_AfterMcpPluginBuild()
        {
            // Arrange
            var reflector = new Reflector();
            using var plugin = new McpPluginBuilder(new Version())
                .AddLogging(b => { })
                .Build(reflector);

            var dto = new TestDto
            {
                PascalCaseProperty = "TestValue",
                AnotherProperty = 123
            };

            // Act
            var json = reflector.JsonSerializer.Serialize(dto);

            // Assert
            json.ShouldContain("\"PascalCaseProperty\": \"TestValue\"");
            json.ShouldContain("\"AnotherProperty\": 123");
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
            using var plugin = new McpPluginBuilder(new Version())
                .AddLogging(b => { })
                .Build(reflector);

            var json = $"{{\"{jsonPropertyName}\": \"TestValue\"}}";

            // Act
            var result = reflector.JsonSerializer.Deserialize<TestDto>(json);

            // Assert
            result.ShouldNotBeNull();
            result!.PascalCaseProperty.ShouldBe("TestValue");
        }
    }
}
