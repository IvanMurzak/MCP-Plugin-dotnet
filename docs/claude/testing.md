# Testing Conventions

- xUnit + Shouldly + Moq
- Arrange-Act-Assert pattern
- `[Fact]` for single scenarios, `[Theory]` for parameterized
- `[Collection("McpPlugin")]` for test isolation
- Custom `TestLoggerFactory` / `XunitTestOutputLoggerProvider` for test logging

## Test commands

```bash
# Run all tests
dotnet test

# Run a specific test project
dotnet test McpPlugin.Tests/McpPlugin.Tests.csproj

# Run a specific test class or method
dotnet test --filter "ClassName=McpBuilderTests"
dotnet test --filter "FullyQualifiedName~McpBuilderTests.Build_WithoutLogging_ShouldSucceed"
```
