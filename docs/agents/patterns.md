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

### Global skill metadata

Set host-owned metadata once, before `Build()`:

```csharp
builder.WithConfig(config =>
{
    config.SkillMetadata["generated-by"] = "my-plugin";
    config.SkillMetadata["my-client"] = "exclude";
});
```

The built-in `SkillFileGenerator` and subclasses using its document builders apply this mapping
to every generated tool page and standalone `[AiSkill]` document, including initial generation.
Entries override the default `generated-by: mcp-plugin-dotnet`; unconfigured callers keep the
existing format. `SkillMetadata` is runtime-only and is not written to connection settings.
Metadata has no filtering semantics in this library: consumers interpret their own keys.
Independent generators can reuse `SkillFileGenerator.AppendMetadata` with the same mapping.
Custom document-builder overrides should call it with the generator's `Metadata` property.
