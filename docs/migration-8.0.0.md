# Migrating to 8.0.0

`8.0.0` is a **major** release: three methods across two public `McpPlugin.Common` hub interfaces
gained a `CancellationToken` parameter. Callers keep compiling (the parameter is defaulted), but
**implementers do not**, and the change is binary-breaking either way — which is why this is
`8.0.0` rather than a patch.

> **The version constant is not bumped by this document.** `<Version>` in
> `McpPlugin/McpPlugin.csproj` is the single source of truth for all three packages, and pushing a
> new value to `main` is what triggers `release.yml` to publish to NuGet (see
> `docs/claude/release.md`). The bump to `8.0.0` is therefore made by the release run, via
> `commands/bump-version.ps1 8.0.0`, not by a feature PR.

## What changed

Introduced in [#194](https://github.com/IvanMurzak/MCP-Plugin-dotnet/pull/194), which bounded the
prompt/resource list paths with the same linked-CTS timeout the tool path already used. Threading a
token through those paths required it on the hub contracts:

| Interface | Method |
|---|---|
| `IClientPromptHub` | `RunListPrompts` |
| `IClientResourceHub` | `RunListResources` |
| `IClientResourceHub` | `RunResourceTemplates` |

All three now end in `CancellationToken cancellationToken = default`:

```csharp
// McpPlugin.Common/src/Hub/Client/IClientPromptHub.cs
Task<ResponseData<ResponseListPrompts>> RunListPrompts(
    RequestListPrompts request, CancellationToken cancellationToken = default);

// McpPlugin.Common/src/Hub/Client/IClientResourceHub.cs
Task<ResponseData<ResponseListResource[]>> RunListResources(
    RequestListResources request, CancellationToken cancellationToken = default);

Task<ResponseData<ResponseResourceTemplate[]>> RunResourceTemplates(
    RequestListResourceTemplates request, CancellationToken cancellationToken = default);
```

## Who has to change

**Implementers of these interfaces** — any downstream host that supplies its OWN `IClientPromptHub` /
`IClientResourceHub` implementation. Such an implementation no longer satisfies the interface and
fails to compile with `CS0535` ("does not implement interface member").

**The engine plugins (Unity-MCP, Godot-MCP, Unreal-MCP) are NOT implementers** and will not see a
`CS0535`. They CONSUME `IPromptManager` / `IResourceManager` from the `McpPlugin` package, and the
concrete managers behind those already carried the token overload — which is why #194 was able to
change both interfaces without touching a single implementation in this repository. For the engine
plugins 8.0.0 is a pin bump plus a recompile (next paragraph), not a source change.

**Callers need no source change.** The parameter is defaulted, so existing call sites still compile.
They must still be **recompiled** against 8.0.0: a defaulted parameter is baked into the caller at
compile time, so a binary built against 7.x will `MissingMethodException` at runtime against the
8.0.0 assembly. Do not mix 7.x and 8.0.0 assemblies in one process.

## How to migrate

`CancellationToken` lives in `System.Threading`, and `ImplicitUsings` is **disabled** in every
project here — so add `using System.Threading;` to any file that does not already have it, or the
new parameter will not resolve.

Add the parameter to each override and honour it:

```diff
-public Task<ResponseData<ResponseListPrompts>> RunListPrompts(RequestListPrompts request)
+public Task<ResponseData<ResponseListPrompts>> RunListPrompts(
+    RequestListPrompts request, CancellationToken cancellationToken = default)
 {
-    return _promptManager.RunListPrompts(request);
+    return _promptManager.RunListPrompts(request, cancellationToken);
 }
```

If the underlying work genuinely cannot be cancelled, accept the parameter and ignore it rather than
omitting it — the signature must match. Pass the token down wherever the call chain already accepts
one.

**Keeping the 1-argument form as an overload is fine, and is what this repo does.** Every in-repo
implementer declares the 2-argument member plus a 1-argument overload delegating to it, so any
concrete-typed caller keeps compiling unchanged (`McpPlugin.Server/src/Client/RemotePromptRunner.cs`):

```csharp
public Task<ResponseData<ResponseListPrompts>> RunListPrompts(RequestListPrompts request)
    => RunListPrompts(request, default);

public async Task<ResponseData<ResponseListPrompts>> RunListPrompts(
    RequestListPrompts request, CancellationToken cancellationToken = default) { /* … */ }
```

## Also in 8.0.0

- `select_engine_instance` now actually takes effect. Its per-session selection is honoured by
  routing on every **subsequent** request of that MCP session, not just the request that set it
  ([#195](https://github.com/IvanMurzak/MCP-Plugin-dotnet/issues/195)). Additive API only
  (`AccountMcpStrategy.Selections`, plus a two-argument constructor); no existing signature changed,
  so this part is neither source- nor binary-breaking. A session that relied on the previous (broken)
  behaviour was in practice being routed to the most-recently-active instance.
- `resources/list` and `resources/templates/list` degrade to an empty catalog instead of raising a
  JSON-RPC error when no engine plugin is connected (#194).

## Downstream pins to move in lockstep

`com.IvanMurzak.McpPlugin`, `com.IvanMurzak.McpPlugin.Server` and `com.IvanMurzak.McpPlugin.Common`
share one `<Version>` and are published together. Consumers that pin more than one of them must move
**every** pin to `8.0.0` in the same change — a split pin mixes 7.x and 8.0.0 assemblies, which is
exactly the binary-incompatible combination described above.
