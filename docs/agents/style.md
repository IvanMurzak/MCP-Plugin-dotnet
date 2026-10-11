# Code Style (Mandatory)

- **Namespaces**: Java-style reverse domain — `com.IvanMurzak.McpPlugin.*` (not standard C# convention)
- **File headers**: ALL `.cs` files must start with the ASCII art license header. Copy from any existing file.
- **Formatting**: Allman braces, `_camelCase` private fields, `PascalCase` public members
- **Language**: `LangVersion` 9.0 for main libraries, 11.0 for test projects. `ImplicitUsings` disabled, `Nullable` enabled
- **Reactive**: Use `R3` library (`Subject<T>`, `Observable<T>`) for event handling
- **Reflection**: Use `com.IvanMurzak.ReflectorNet` over `System.Reflection`
- **Logging**: `Microsoft.Extensions.Logging` abstractions (NLog backend on server)
- **DI**: `Microsoft.Extensions.DependencyInjection`
- **Disposal**: Use `CompositeDisposable` pattern with `.AddTo(_disposables)`
- **CLI args**: Use `DataArguments` class, not raw `IConfiguration`
