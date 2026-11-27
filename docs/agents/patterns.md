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

### Tool annotations (title and hints)

Give every tool a `Title` and an explicit `ReadOnlyHint` or `DestructiveHint` (also `IdempotentHint` /
`OpenWorldHint` where known): MCP tool directories (e.g. the Claude Connectors Directory) reject tools without
them. An unset hint is omitted from `tools/list`, never defaulted. Guard it with a unit test in the engine repo:

```csharp
[AiTool("scene-save", Title = "Save scene", ReadOnlyHint = false, DestructiveHint = true, IdempotentHint = true, OpenWorldHint = false)]

// test: lists each tool missing a title, missing both safety hints, or marked read-only AND destructive
AiToolAnnotationsValidator.Validate(typeof(MyTools).Assembly).ShouldBeEmpty();
AiToolAnnotationsValidator.ThrowIfAny(AiToolAnnotationsValidator.Validate(typeof(MyTools).Assembly)); // framework-neutral
AiToolAnnotationsValidator.ValidateTools(listedTools).ShouldBeEmpty(); // a RunListTool catalogue, incl. AddTool tools
```

### Throwing from a tool

An `[AiTool]` method that throws is reported as the caller's fault (kind `BadRequest`, HTTP 422 on
`POST /api/tools/{name}`) unless the exception is a fault type (`NullReferenceException`, `TimeoutException`, `OperationCanceledException` and
the others listed in `docs/tool-errors.md`, subclasses included), which stays
`Internal` / 500. So throw a plain or custom exception with an actionable message when the request cannot be done,
and never let a fault type escape for bad input. When the host cannot serve the call right now (compiling, reloading,
not initialized), throw `ToolUnavailableException` instead: `Unavailable` / 503; add `notExecuted: true` only if the
tool changed nothing yet (an 8.14+ server then re-sends the call). The rule lives in `RunTool.IsFrameworkFault` and
`RunTool.ExecutionFailed`; the full table is in `docs/tool-errors.md`.

### Game-only (runtime) and editor-only tools

A tool's `Target` (`ToolTarget.Both` by default, `Editor`, `Runtime`) says which plugin connections register it.
Set it on the class (`[AiToolType(Target = ToolTarget.Runtime)]`) or per tool (`[AiTool("name", Target = ...)]`,
which wins). At `Build()` a plugin skips every tool whose target does not include its role
(`ConnectionConfig.ClientRole`, or a handshake environment ending in `(runtime)`), so set the role before
`Build()`.

```csharp
using com.IvanMurzak.McpPlugin.Common; // ToolTarget

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

The role is read once, at `Build()`: tools added later (`IToolManager.AddTool`) are not filtered. The editor's
generated SKILL.md files no longer include `Runtime` tools (they are not registered there); a game build that
generates skills includes them.

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

### Per-kind skill metadata

A tool page (an `[AiTool]` method, standard or system) and a standalone skill (an `[AiSkill]`
property on an `[AiSkillType]` class) can carry different metadata. Two overlays sit on top of
`SkillMetadata`; an overlay entry replaces a shared entry with the same key:

```csharp
builder.WithConfig(config =>
{
    config.SkillMetadata["generated-by"] = "my-plugin";          // both kinds
    config.ToolSkillMetadata["my-client"] = "exclude";           // tool pages only
    config.StandaloneSkillMetadata["my-client"] = "include";     // standalone skills only
});
```

Both overlays are empty by default, so a host that sets only `SkillMetadata` gets the same files as
before. The generator exposes the merged mappings as `EffectiveToolSkillMetadata` and
`EffectiveStandaloneSkillMetadata`; independent generators can merge with
`SkillFileGenerator.MergeMetadata(shared, overlay)` and write the result with
`SkillFileGenerator.AppendMetadata`. Custom document-builder overrides that rebuild the front matter
should pass the matching `Effective*SkillMetadata` property (`BuildMarkdown`: tool,
`BuildSkillContentMarkdown`: standalone).
