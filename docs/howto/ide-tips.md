# IDE Tips — Pragmatic.Design

> Workarounds and best practices for working with Source Generators in the monorepo.

## Source Generator — IntelliSense lag

The SG generates code at compile time. The IDE may not refresh IntelliSense immediately after a change to the attributes.

### Workaround

| IDE | Fix |
|-----|-----|
| **Visual Studio** | `Build → Rebuild Solution` or `Ctrl+Shift+B` |
| **Rider** | `File → Invalidate Caches → Just Restart` or `Build → Rebuild Solution` |
| **VS Code + C# Dev Kit** | `Ctrl+Shift+P` → `OmniSharp: Restart OmniSharp` |
| **CLI** | `dotnet build` in the project directory |

### If the generated code does not appear

1. **Check that the SG is referenced** in the `.csproj`:
   ```xml
   <ProjectReference Include="...\Pragmatic.SourceGenerator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
   ```

2. **Check the trigger attribute** — the SG generates code only when it finds specific attributes (`[Entity]`, `[DomainAction]`, `[MessageHandler]`, etc.)

3. **Check that the class is `partial`** — almost every SG requires partial classes

4. **Check the build output**:
   ```bash
   dotnet build -v detailed 2>&1 | grep "Pragmatic.SourceGenerator"
   ```

5. **Inspect the generated files**:
   ```
   obj/Debug/net10.0/generated/Pragmatic.SourceGenerator/
   ```

## Opening the project

| Scenario | Solution to open |
|----------|------------------|
| Full framework | `Pragmatic.Design.slnx` (root) |
| Single module | `Pragmatic.{Module}/Pragmatic.{Module}.slnx` |
| Showcase app | `examples/showcase/Showcase.slnx` |
| SG only | `Pragmatic.SourceGenerator/Pragmatic.SourceGenerator.slnx` |

**Tip**: opening the root solution can be slow (30+ projects). For day-to-day work on a module, use its dedicated solution.

## Debugging the Source Generator

### Visual Studio
1. Open `Pragmatic.SourceGenerator.csproj`
2. Add `Debugger.Launch()` in the `Initialize()` method of `PragmaticSourceGenerator`
3. Build the consumer project → the debugger attaches

### Rider
1. Open the consumer module's solution
2. `Run → Attach to Process` → look for `dotnet` with the `build` argument
3. Set a breakpoint in the SG → rebuild

### CLI (diagnostics only)
```bash
# Show all the generated PRAG diagnostics
dotnet build 2>&1 | grep "PRAG"

# Show the generated files
ls obj/Debug/net10.0/generated/Pragmatic.SourceGenerator/
```

## Performance tips

### Incremental build
The SG uses `IIncrementalGenerator` — only the modified files are regenerated. But if you change a shared attribute, everything is regenerated.

```bash
# Build only the current module (fast)
dotnet build Pragmatic.Actions/src/Pragmatic.Actions/

# Incremental build from the root (slower, but catches cross-module effects)
dotnet build Pragmatic.Design.slnx
```

### Tests
```bash
# Tests of a single module (fast)
dotnet test Pragmatic.Actions/tests/ --no-build

# Tests with a filter (faster)
dotnet test --filter "FullyQualifiedName~MyTest" --no-build

# Every suite: the gate, never `dotnet test` on the whole solution
node scripts/check.mjs --tier all
```

`dotnet test` on the solution starts the container suites concurrently and saturates Docker; the gate
runs them one at a time after a clean build. See [TESTING.md](../TESTING.md).

## .editorconfig — naming rules

The project uses `.editorconfig` with strict rules:

| Kind | Convention | Example |
|------|------------|---------|
| Public member | PascalCase | `GetAsync()` |
| Private field | _camelCase | `_repository` |
| Parameter | camelCase | `cancellationToken` |
| Constant | PascalCase | `DefaultTimeout` |

**Tip**: if the IDE shows naming warnings, check that `.editorconfig` is picked up. In VS Code, you may need the EditorConfig extension.

## Hot Reload

Hot reload **does not work** with Source Generators — changes to attributes or models require a rebuild. This is a Roslyn limitation, not a framework one.

**Workaround**: for fast iteration on runtime code (not SG), hot reload works normally with `dotnet watch`.

```bash
# For the Showcase app
dotnet watch run --project examples/showcase/src/Showcase.Host/
```

## Common troubleshooting

| Problem | Cause | Fix |
|---------|-------|-----|
| `error CS0246: type 'X' not found` | The SG did not generate the code | Rebuild, check attribute + partial |
| `warning PRAG0400` | Class not partial | Add the `partial` keyword |
| IntelliSense red but build OK | Stale IDE cache | Restart the IDE / OmniSharp |
| Snapshot test `.received.txt` differs | Generated code changed | Copy `.received.txt` → `.verified.txt` if correct |
| Slow build (>30s) | Full solution rebuild | Use the per-module solution, not the root |
| `The type 'X' exists in both assemblies` | Abstractions conflict | Check that a single package provides the type |
