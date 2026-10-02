# Pragmatic.SourceGen.Testing

Shared test infrastructure for source generator tests.

## Usage

Link these files into your test project:

```xml
<ItemGroup>
  <Compile Include="$(SolutionDir)shared\SourceGen\Testing\*.cs" Link="Helpers\%(Filename)%(Extension)" />
</ItemGroup>
```

## Example

```csharp
using Pragmatic.SourceGen.Testing;

public class MyGeneratorTests
{
    [Fact]
    public void Generator_WithValidInput_GeneratesCode()
    {
        var source = """
            [MyAttribute]
            public class MyClass { }
            """;

        // Run generator with module-specific references
        var result = GeneratorTestHelper.RunGenerator<MySourceGenerator>(
            source,
            GeneratorTestHelper.FromType<MyAttributeType>());

        // Assertions
        Assert.True(result.HasGeneratedFiles);
        Assert.False(GeneratorTestHelper.HasCompilationErrors(result));

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "MyClass");
        Assert.Contains("expected code", generated);
    }
}
```

## API

| Method | Description |
|--------|-------------|
| `RunGenerator<T>(source, refs)` | Run generator with additional references |
| `GetGeneratedSource(result, hint)` | Get source by hint name |
| `GetGeneratedSourcesAsDictionary(result)` | Get all sources as dictionary |
| `HasCompilationErrors(result)` | Check for compilation errors |
| `GetCompilationErrors(result)` | Get all compilation errors |
| `HasDiagnostic(result, id)` | Check for specific diagnostic |
| `GetGeneratorDiagnostics(result, prefix)` | Get diagnostics by prefix |
| `FromType<T>()` | Create reference from type's assembly |
| `FromTypeAssembly(type)` | Create reference from Type (for static types) |
| `TryGetAssemblyReference(name)` | Try to get assembly by name |
