// Pragmatic.SourceGen.Testing - Generator Test Helper
// This file is linked into test projects, not compiled directly.

using System.Linq.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Pragmatic.SourceGen.Testing;

/// <summary>
///     Helper for running source generator tests.
/// </summary>
public static class GeneratorTestHelper
{
    /// <summary>
    ///     Runs a source generator on the given source code.
    /// </summary>
    /// <typeparam name="TGenerator">The type of the incremental generator.</typeparam>
    /// <param name="source">The C# source code to compile.</param>
    /// <param name="additionalReferences">Optional additional metadata references.</param>
    /// <returns>The generator run result.</returns>
    public static SourceGenRunResult RunGenerator<TGenerator>(
        string source,
        params MetadataReference[] additionalReferences)
        where TGenerator : IIncrementalGenerator, new()
    {
        return RunGenerator(new TGenerator(), source, additionalReferences);
    }

    /// <summary>
    ///     Runs a source generator on the given source code.
    /// </summary>
    /// <param name="generator">The incremental generator instance.</param>
    /// <param name="source">The C# source code to compile.</param>
    /// <param name="additionalReferences">Optional additional metadata references.</param>
    /// <returns>The generator run result.</returns>
    public static SourceGenRunResult RunGenerator(
        IIncrementalGenerator generator,
        string source,
        params MetadataReference[] additionalReferences)
    {
        return RunGenerator(generator, source, OutputKind.DynamicallyLinkedLibrary, additionalReferences);
    }

    /// <summary>
    ///     Runs a source generator with in-memory <c>AdditionalFiles</c> — a <c>roles.pragmatic.json</c>, a
    ///     translation file — beside the source.
    /// </summary>
    /// <param name="source">The C# source code to compile.</param>
    /// <param name="files">The additional files, as (path, content).</param>
    /// <param name="additionalReferences">Optional additional metadata references.</param>
    public static SourceGenRunResult RunGeneratorWithFiles<TGenerator>(
        string source,
        IEnumerable<(string Path, string Content)> files,
        params MetadataReference[] additionalReferences)
        where TGenerator : IIncrementalGenerator, new()
    {
        return RunGenerator(new TGenerator(), source, OutputKind.DynamicallyLinkedLibrary, additionalReferences,
            [.. files.Select(f => (AdditionalText)new InMemoryAdditionalText(f.Path, f.Content))]);
    }

    /// <summary>
    ///     Runs a source generator in host mode: the compilation is an executable
    ///     (ConsoleApplication), so <c>CompositionDetector.IsHostProject</c> is satisfied
    ///     when the source also declares an entry point. Use for PragmaticHost template tests.
    /// </summary>
    /// <typeparam name="TGenerator">The type of the incremental generator.</typeparam>
    /// <param name="source">The C# source code to compile (must include a Main method or top-level statements).</param>
    /// <param name="additionalReferences">Optional additional metadata references.</param>
    /// <returns>The generator run result.</returns>
    public static SourceGenRunResult RunGeneratorAsHost<TGenerator>(
        string source,
        params MetadataReference[] additionalReferences)
        where TGenerator : IIncrementalGenerator, new()
    {
        return RunGenerator(new TGenerator(), source, OutputKind.ConsoleApplication, additionalReferences);
    }

    /// <summary>
    ///     Compiles <paramref name="source" /> as its own assembly and returns it as a reference, so a
    ///     test can put a declaration <em>outside</em> the compilation the generator runs on.
    /// </summary>
    /// <remarks>
    ///     A generator that resolves a type by walking its own assembly's namespaces cannot see one
    ///     that arrives through a reference; the only way to test that seam is to have a reference.
    /// </remarks>
    public static MetadataReference CompileReference(
        string assemblyName,
        string source,
        params MetadataReference[] additionalReferences)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, path: $"{assemblyName}.cs")],
            GetBaseReferences().Concat(additionalReferences).ToArray(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"The referenced assembly '{assemblyName}' does not compile: "
                + string.Join("; ", errors.Select(e => e.ToString())));

        return compilation.ToMetadataReference();
    }

    /// <summary>
    ///     Runs the generator over <paramref name="source"/> and returns the result as a reference, so a
    ///     second compilation can be generated <em>against a module that was itself generated</em>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="CompileReference" /> is not enough for a feature whose input is another
    ///         assembly's <b>generated</b> output — an assembly-level metadata attribute, a static list,
    ///         a manifest. <c>[ReadAccess&lt;T&gt;]</c> is the case that forced this: the reading
    ///         context is built from <c>PragmaticModuleMetadataAttribute</c>, which the generator emits,
    ///         so a single compilation produces neither the <c>DbSet</c> nor the
    ///         <c>ExcludeFromMigrations</c> line and a test written that way asserts nothing.
    ///     </para>
    ///     <para>
    ///         The assembly name is a parameter because <see cref="RunGenerator{TGenerator}(string, MetadataReference[])" />
    ///         hard-codes <c>TestAssembly</c>, and two compilations cannot both be called that when one
    ///         references the other.
    ///     </para>
    ///     <para>
    ///         Compilation errors throw rather than being returned: a referenced module that does not
    ///         build makes every assertion downstream a statement about nothing.
    ///     </para>
    /// </remarks>
    public static MetadataReference RunGeneratorAsReference<TGenerator>(
        string assemblyName,
        string source,
        params MetadataReference[] additionalReferences)
        where TGenerator : IIncrementalGenerator, new()
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, path: $"{assemblyName}.cs")],
            GetBaseReferences().Concat(additionalReferences).ToArray(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new TGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _);

        var errors = generated.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"The generated referenced assembly '{assemblyName}' does not compile: "
                + string.Join("; ", errors.Select(e => e.ToString())));

        return generated.ToMetadataReference();
    }

    private static SourceGenRunResult RunGenerator(
        IIncrementalGenerator generator,
        string source,
        OutputKind outputKind,
        MetadataReference[] additionalReferences,
        AdditionalText[]? additionalTexts = null)
    {
        // Parse with a file path so the tree has a non-empty FilePath. LocationInfo.From returns null
        // for a path-less tree, which makes diagnostics guarded on a non-null location silently
        // suppress themselves under test (real compilations always have a file path).
        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: "TestSource.cs");
        var references = GetBaseReferences().Concat(additionalReferences).ToArray();

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(outputKind)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [generator.AsSourceGenerator()], additionalTexts: additionalTexts ?? []);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();

        return new SourceGenRunResult(runResult, outputCompilation, diagnostics);
    }

    /// <summary>
    ///     Runs the generator twice to exercise incremental caching: first on <paramref name="source"/>,
    ///     then on a compilation that adds an UNRELATED class (<paramref name="unrelatedAddition"/>). A
    ///     value-equatable pipeline must keep the original entity's tracked steps <c>Cached</c>/<c>Unchanged</c>
    ///     on the second run — if a step combines the raw <c>CompilationProvider</c> or carries a non-equatable
    ///     model, it spuriously re-executes (reported by <see cref="GetReExecutedSteps"/>).
    /// </summary>
    public static IncrementalRunResult RunGeneratorIncremental<TGenerator>(
        string source,
        string unrelatedAddition,
        params MetadataReference[] additionalReferences)
        where TGenerator : IIncrementalGenerator, new()
    {
        var references = GetBaseReferences().Concat(additionalReferences).ToArray();
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithNullableContextOptions(NullableContextOptions.Enable);

        var compilation = CSharpCompilation.Create(
            "TestAssembly", [CSharpSyntaxTree.ParseText(source)], references, options);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new TGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);

        // Second compilation: same entity sources + one unrelated class. The unrelated change forces a new
        // Compilation instance, so only properly value-equatable steps stay cached.
        var compilation2 = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(unrelatedAddition));
        driver = driver.RunGenerators(compilation2);

        return new IncrementalRunResult(driver.GetRunResult().Results[0]);
    }

    /// <summary>
    ///     Returns the tracked steps that re-executed on the second incremental run (i.e. were not
    ///     <c>Cached</c>/<c>Unchanged</c>) among the given <paramref name="trackingNames"/>.
    /// </summary>
    public static IReadOnlyList<string> GetReExecutedSteps(IncrementalRunResult result, params string[] trackingNames)
    {
        var reexecuted = new List<string>();
        foreach (var name in trackingNames)
        {
            if (!result.RunResult.TrackedSteps.TryGetValue(name, out var steps))
                continue;

            foreach (var step in steps)
            foreach (var output in step.Outputs)
            {
                if (output.Reason is not (IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged))
                    reexecuted.Add($"{name}={output.Reason}");
            }
        }

        return reexecuted;
    }

    /// <summary>
    ///     Gets the generated source for a specific hint name.
    /// </summary>
    public static string? GetGeneratedSource(SourceGenRunResult result, string hintNameContains)
    {
        // Prefer exact artifact match: {Type}.{hintNameContains}.g.cs
        //
        // ⚠️ "Exact" is not unique. The per-assembly convention is _Infra.{Category}.{Extension}.g.cs,
        // so every category that emits a registration ends with ".Registration.g.cs" and a hint of
        // "Registration" matches whichever comes first. Adding the Redaction feature made four
        // Endpoints tests read the wrong file. Pass the category too — "Endpoints.Registration".
        var exactMatch = result.GeneratedTrees.FirstOrDefault(t =>
            Path.GetFileName(t.FilePath).EndsWith($".{hintNameContains}.g.cs", StringComparison.OrdinalIgnoreCase));

        if (exactMatch is not null)
            return exactMatch.GetText().ToString();

        // Fallback: any file containing the hint name (original behavior)
        var matchingTree = result.GeneratedTrees.FirstOrDefault(t =>
            t.FilePath.Contains(hintNameContains, StringComparison.OrdinalIgnoreCase));

        return matchingTree?.GetText().ToString();
    }

    /// <summary>
    ///     Gets all generated sources as a dictionary (hint name -> source).
    /// </summary>
    public static Dictionary<string, string> GetGeneratedSourcesAsDictionary(SourceGenRunResult result)
    {
        return result.GeneratedTrees
            .ToDictionary(
                t => Path.GetFileName(t.FilePath),
                t => t.GetText().ToString());
    }

    /// <summary>
    ///     Checks if the compilation has any errors after generation.
    /// </summary>
    public static bool HasCompilationErrors(SourceGenRunResult result)
    {
        return result.OutputCompilation.GetDiagnostics()
            .Any(d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>
    ///     Gets compilation errors after generation.
    /// </summary>
    public static IEnumerable<Diagnostic> GetCompilationErrors(SourceGenRunResult result)
    {
        return result.OutputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>
    ///     Gets compilation warnings after generation.
    /// </summary>
    public static IEnumerable<Diagnostic> GetCompilationWarnings(SourceGenRunResult result)
    {
        return result.OutputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Warning);
    }

    /// <summary>
    ///     Gets generator-specific diagnostics with the given prefix.
    /// </summary>
    public static IEnumerable<Diagnostic> GetGeneratorDiagnostics(SourceGenRunResult result, string idPrefix = "PRAG")
    {
        return result.Diagnostics
            .Where(d => d.Id.StartsWith(idPrefix, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Checks if a specific diagnostic ID was emitted.
    /// </summary>
    public static bool HasDiagnostic(SourceGenRunResult result, string diagnosticId)
    {
        return result.Diagnostics.Any(d => d.Id == diagnosticId) ||
               result.OutputCompilation.GetDiagnostics().Any(d => d.Id == diagnosticId);
    }

    /// <summary>
    ///     Gets all diagnostics with a specific ID.
    /// </summary>
    public static IEnumerable<Diagnostic> GetDiagnosticsById(SourceGenRunResult result, string diagnosticId)
    {
        return result.Diagnostics.Where(d => d.Id == diagnosticId)
            .Concat(result.OutputCompilation.GetDiagnostics().Where(d => d.Id == diagnosticId));
    }

    /// <summary>
    ///     Gets base metadata references needed for compilation.
    /// </summary>
    private static IEnumerable<MetadataReference> GetBaseReferences()
    {
        var runtimePath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        yield return MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(Console).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(Expression).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(Task).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(IServiceProvider).Assembly.Location);
        yield return MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Runtime.dll"));
        yield return MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Collections.dll"));
        yield return MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.ComponentModel.dll"));
        yield return MetadataReference.CreateFromFile(Path.Combine(runtimePath, "netstandard.dll"));
    }

    /// <summary>
    ///     Tries to add an assembly reference by name if available.
    /// </summary>
    public static MetadataReference? TryGetAssemblyReference(string assemblyName)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == assemblyName);

        if (assembly is null)
        {
            // Not loaded yet: force-load by name. Relying on AppDomain state alone made the
            // reference set depend on which OTHER tests ran first (order-dependent flakiness).
            try
            {
                assembly = System.Reflection.Assembly.Load(new System.Reflection.AssemblyName(assemblyName));
            }
            catch (Exception e) when (e is System.IO.FileNotFoundException or System.IO.FileLoadException or BadImageFormatException)
            {
                return null;
            }
        }

        return MetadataReference.CreateFromFile(assembly.Location);
    }

    /// <summary>
    ///     Creates metadata reference from a type's assembly.
    /// </summary>
    public static MetadataReference FromType<T>()
    {
        return MetadataReference.CreateFromFile(typeof(T).Assembly.Location);
    }

    /// <summary>
    ///     Creates metadata reference from a Type's assembly.
    ///     Use this for static types that cannot be used as type arguments.
    /// </summary>
    /// <example>
    ///     // For static class Pragmatic.Ensure.Ensure:
    ///     GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Ensure.Ensure))
    /// </example>
    public static MetadataReference FromTypeAssembly(Type type)
    {
        return MetadataReference.CreateFromFile(type.Assembly.Location);
    }
}