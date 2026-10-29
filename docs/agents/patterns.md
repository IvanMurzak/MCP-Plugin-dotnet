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

### Game-only (runtime) and editor-only tools

A tool's `Target` (`ToolTarget.Both` by default, `Editor`, `Runtime`) says which plugin connections register it.
Set it on the class (`[AiToolType(Target = ToolTarget.Runtime)]`) or per tool (`[AiTool("name", Target = ...)]`,
which wins). At `Build()` a plugin skips every tool whose target does not include its role
(`ConnectionConfig.ClientRole`, or a handshake environment ending in `(runtime)`), so set the role before
`Build()`.

```csharp
[AiToolType(Target = ToolTarget.Runtime)] // the editor scans this assembly too, but never registers these
public static class GameTools
{
    [AiTool("game-scene-tree-summary")]
    public static string SceneTree() => ...;
}
```

Mark every game-only tool `Runtime`: engines that compile the game and the editor tools into one assembly (Godot)
otherwise register it in the editor as well, and the editor answers it. The server routes a call of a tool that
only the runtime has (listed as `Runtime`, or not listed by the editor) to the runtime even while the editor is
connected, and adds such tools to `tools/list`. The target travels as `ResponseListTool.Target` (null = `Both`).

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
