# Registration Patterns

### Server registration pattern
```csharp
builder.Services.WithMcpServer(dataArguments).WithMcpPluginServer(dataArguments);
app.UseMcpPluginServer(dataArguments);
```

### Client builder pattern
```csharp
new McpPluginBuilder(version)
    .WithConfig(config => config.Host = "http://localhost:8080")
    .WithToolsFromAssembly(typeof(MyTools).Assembly)
    .Build(reflector);
```

### Attribute-based component registration
```csharp
[AiToolType]
public static class MyTools
{
    [AiTool("add", "Adds two numbers")]
    public static int Add(int a, int b) => a + b;
}
```

The legacy `[McpPluginToolType]` / `[McpPluginTool]` / `[McpPluginPrompt]` / `[McpPluginResource]` / `[McpPluginSkill]` names remain available as `[Obsolete]` subclass aliases of the new `[Ai*]` types, so existing consumer code continues to compile (with a deprecation warning) until callers migrate to the new names.
