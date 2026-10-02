// Pragmatic.Composition.HostWiring.Tests - A module and the host that includes it

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     Compiles a module into a library through the generator, then a host that references it, the way
///     an application is built: two assemblies, the host reading the module's generated metadata.
/// </summary>
/// <remarks>
///     For tests about what the host does with its <c>[Include&lt;TModule, TDatabase&gt;]</c> topology,
///     which <see cref="HostWiringFixture" />'s probe host does not declare.
/// </remarks>
internal static class ModuleAndHost
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    /// <summary>
    ///     The host's compilation errors and its generated files keyed by hint name.
    /// </summary>
    public static (ImmutableArray<string> Errors, IReadOnlyDictionary<string, string> Generated) Generate(
        string moduleAssemblyName,
        IEnumerable<string> moduleSources,
        string hostAssemblyName,
        string hostSource)
    {
        var (errors, _, generated) = Compile(moduleAssemblyName, moduleSources, hostAssemblyName, hostSource);
        return (errors, generated);
    }

    /// <summary>
    ///     The compiler's warnings inside the files the generator wrote into the host.
    /// </summary>
    /// <remarks>
    ///     An application that builds with <c>-warnaserror</c> — the bar the examples are held to — fails
    ///     on these as it does on an error, and it cannot fix them: the file is not its own.
    /// </remarks>
    public static ImmutableArray<string> GeneratedWarnings(
        string moduleAssemblyName,
        IEnumerable<string> moduleSources,
        string hostAssemblyName,
        string hostSource)
        => Compile(moduleAssemblyName, moduleSources, hostAssemblyName, hostSource).GeneratedWarnings;

    /// <summary>
    ///     The module and the host as assembly images, for a test that has to run what was generated.
    /// </summary>
    /// <remarks>
    ///     The host's errors come back beside the images: an image is emitted only when there are none,
    ///     and the caller asserts on them first so a red test names the compiler's reason.
    /// </remarks>
    public static (ImmutableArray<string> Errors, byte[] Module, byte[]? Host) Emit(
        string moduleAssemblyName,
        IEnumerable<string> moduleSources,
        string hostAssemblyName,
        string hostSource)
    {
        var (errors, _, _, module, host) = CompileAll(moduleAssemblyName, moduleSources, hostAssemblyName, hostSource);

        if (!errors.IsEmpty)
            return (errors, module, null);

        using var image = new MemoryStream();
        var emit = host.Emit(image);
        return ([.. emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(Describe)],
            module, emit.Success ? image.ToArray() : null);
    }

    private static (ImmutableArray<string> Errors, ImmutableArray<string> GeneratedWarnings,
        IReadOnlyDictionary<string, string> Generated) Compile(
        string moduleAssemblyName,
        IEnumerable<string> moduleSources,
        string hostAssemblyName,
        string hostSource)
    {
        var (errors, generatedWarnings, generated, _, _) =
            CompileAll(moduleAssemblyName, moduleSources, hostAssemblyName, hostSource);
        return (errors, generatedWarnings, generated);
    }

    private static (ImmutableArray<string> Errors, ImmutableArray<string> GeneratedWarnings,
        IReadOnlyDictionary<string, string> Generated, byte[] Module, Compilation Host) CompileAll(
        string moduleAssemblyName,
        IEnumerable<string> moduleSources,
        string hostAssemblyName,
        string hostSource)
    {
        var references = HostWiringFixture.BuildReferenceSet();

        var library = RunGenerator(CSharpCompilation.Create(
            moduleAssemblyName,
            [
                CSharpSyntaxTree.ParseText(ProbeSource.LibraryGlobalUsings, ParseOptions, "GlobalUsings.cs"),
                .. moduleSources.Select((source, i) => CSharpSyntaxTree.ParseText(source, ParseOptions, $"Module{i}.cs"))
            ],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable)));

        using var image = new MemoryStream();
        var emit = library.Emit(image);
        emit.Success.Should().BeTrue(
            "the module has to build, or the host is compiled against nothing: "
            + string.Join("; ", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        // Any name: the host's generated code lives in a namespace named after its assembly, and names what
        // the modules declare in theirs by full name (AHostNamedOutsideItsModulesRootTests).
        var host = RunGenerator(CSharpCompilation.Create(
            hostAssemblyName,
            [
                CSharpSyntaxTree.ParseText(ProbeSource.HostGlobalUsings, ParseOptions, "GlobalUsings.cs"),
                CSharpSyntaxTree.ParseText(hostSource, ParseOptions, "Program.cs")
            ],
            [.. references, MetadataReference.CreateFromImage(image.ToArray())],
            new CSharpCompilationOptions(OutputKind.ConsoleApplication)
                .WithNullableContextOptions(NullableContextOptions.Enable)));

        var diagnostics = host.GetDiagnostics();

        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(Describe)
            .ToImmutableArray();

        var generatedWarnings = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Warning
                        && d.Location.GetLineSpan().Path is { } path
                        && path.EndsWith(".g.cs", StringComparison.Ordinal))
            .Select(Describe)
            .ToImmutableArray();

        var generated = host.SyntaxTrees
            .Where(t => t.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
            .ToDictionary(t => Path.GetFileName(t.FilePath), t => t.GetText().ToString());

        return (errors, generatedWarnings, generated, image.ToArray(), host);
    }

    /// <summary>
    ///     Several module assemblies, each built against the ones before it, then the host against all of
    ///     them: the shape a module that depends on another module's assembly has.
    /// </summary>
    /// <returns>
    ///     The host's compiler errors and the diagnostics the generator reported on the host — a
    ///     generator diagnostic is not in the compilation's own list, so <see cref="Generate" /> does not
    ///     carry it.
    /// </returns>
    public static (ImmutableArray<string> Errors, ImmutableArray<Diagnostic> GeneratorDiagnostics) GenerateChain(
        IReadOnlyList<(string AssemblyName, string[] Sources)> modules,
        string hostAssemblyName,
        string hostSource)
    {
        var references = new List<MetadataReference>(HostWiringFixture.BuildReferenceSet());

        foreach (var (assemblyName, sources) in modules)
        {
            var library = RunGenerator(CSharpCompilation.Create(
                assemblyName,
                [
                    CSharpSyntaxTree.ParseText(ProbeSource.LibraryGlobalUsings, ParseOptions, "GlobalUsings.cs"),
                    .. sources.Select((source, i) => CSharpSyntaxTree.ParseText(source, ParseOptions, $"{assemblyName}{i}.cs"))
                ],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithNullableContextOptions(NullableContextOptions.Enable)));

            using var image = new MemoryStream();
            var emit = library.Emit(image);
            emit.Success.Should().BeTrue(
                $"module {assemblyName} has to build: "
                + string.Join("; ", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

            references.Add(MetadataReference.CreateFromImage(image.ToArray()));
        }

        var hostCompilation = CSharpCompilation.Create(
            hostAssemblyName,
            [
                CSharpSyntaxTree.ParseText(ProbeSource.HostGlobalUsings, ParseOptions, "GlobalUsings.cs"),
                CSharpSyntaxTree.ParseText(hostSource, ParseOptions, "Program.cs")
            ],
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PragmaticSourceGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver.RunGeneratorsAndUpdateCompilation(hostCompilation, out var host, out var generatorDiagnostics);

        var errors = host.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(Describe)
            .ToImmutableArray();

        return (errors, generatorDiagnostics);
    }

    private static string Describe(Diagnostic d)
        => $"{d.Id} {Path.GetFileName(d.Location.GetLineSpan().Path)}: {d.GetMessage()}";

    private static Compilation RunGenerator(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PragmaticSourceGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return output;
    }
}
