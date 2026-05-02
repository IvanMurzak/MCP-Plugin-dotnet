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
[McpPluginToolType]
public static class MyTools
{
    [McpPluginTool("add", "Adds two numbers")]
    public static int Add(int a, int b) => a + b;
}
```
