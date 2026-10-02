// Pragmatic.Composition.HostWiring.Tests - Fixture
// Compiles the two host shapes under comparison and exposes their generated output.

using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     Builds, once per test collection, the two generated hosts that the suite compares.
/// </summary>
/// <remarks>
///     <para>
///         <b>Subject</b> — a host compilation that contains <see cref="ProbeSource.Declarations" />
///         in its own syntax trees.
///     </para>
///     <para>
///         <b>Control</b> — the same declarations compiled into a library, emitted to an assembly,
///         and referenced by a host compilation that declares nothing. This is the shape every
///         Pragmatic app in this repository has, and the only one that was ever exercised.
///     </para>
///     <para>
///         The control is what makes a comparison meaningful. Asserting on the subject alone cannot
///         tell "the host does not wire this" apart from "this feature emits nothing at all any
///         more": both look like an absent line.
///     </para>
/// </remarks>
public sealed class HostWiringFixture
{
    private const string LibraryAssemblyName = "ProbeLibrary";
    private const string AuxLibraryAssemblyName = "ProbeAuxLibrary";
    private const string PackageLibraryAssemblyName = "ProbePackageLibrary";
    private const string HostAssemblyName = "ProbeHostApp";

    /// <summary>
    ///     Placeholder both assembly names collapse to, so that a registration emitted against the
    ///     library and the same registration emitted against the host compare equal. This is the one
    ///     axis on which the two compilations legitimately differ.
    /// </summary>
    private const string DeclaringAssemblyPlaceholder = "__DeclaringAssembly__";

    private static readonly CSharpParseOptions ParseOptions =
        new(LanguageVersion.Latest);

    public HostWiringFixture()
    {
        var references = BuildReferenceSet();

        // 1. The declarations as a library, run through the generator and emitted to an assembly.
        var library = CSharpCompilation.Create(
            LibraryAssemblyName,
            [
                CSharpSyntaxTree.ParseText(ProbeSource.LibraryGlobalUsings, ParseOptions, "GlobalUsings.cs"),
                CSharpSyntaxTree.ParseText(ProbeSource.Declarations, ParseOptions, "Probes.cs")
            ],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var generatedLibrary = RunGenerator(library, withTranslations: true);
        LibraryErrors = ErrorsOf(generatedLibrary);

        using var image = new MemoryStream();
        var emit = generatedLibrary.Emit(image);
        LibraryEmitErrors = emit.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(Describe)
            .ToImmutableArray();

        var libraryReference = emit.Success
            ? MetadataReference.CreateFromImage(image.ToArray())
            : null;

        // 1a-bis. The package the left-out module imports, as an assembly of its own: the metadata an
        // importer reads exists only on one.
        var packageLibrary = CSharpCompilation.Create(
            PackageLibraryAssemblyName,
            [
                CSharpSyntaxTree.ParseText(ProbeSource.LibraryGlobalUsings, ParseOptions, "GlobalUsings.cs"),
                CSharpSyntaxTree.ParseText(ProbeSource.PackageDeclarations, ParseOptions, "PackageProbes.cs")
            ],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        using var packageImage = new MemoryStream();
        var packageEmit = RunGenerator(packageLibrary).Emit(packageImage);
        PackageLibraryEmitErrors = packageEmit.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(Describe)
            .ToImmutableArray();

        var packageReference = packageEmit.Success
            ? MetadataReference.CreateFromImage(packageImage.ToArray())
            : null;

        // 1b. A second library, so the suite can express a host that references two modules and
        // includes one. The filter that decides what a host wires works on assembly names, so a second
        // namespace in the first library would not be the same shape.
        MetadataReference[] auxReferences = packageReference is null
            ? references
            : [.. references, packageReference];

        var auxLibrary = CSharpCompilation.Create(
            AuxLibraryAssemblyName,
            [
                CSharpSyntaxTree.ParseText(ProbeSource.LibraryGlobalUsings, ParseOptions, "GlobalUsings.cs"),
                CSharpSyntaxTree.ParseText(ProbeSource.AuxDeclarations, ParseOptions, "AuxProbes.cs")
            ],
            auxReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        using var auxImage = new MemoryStream();
        var auxEmit = RunGenerator(auxLibrary).Emit(auxImage);
        AuxLibraryEmitErrors = auxEmit.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(Describe)
            .ToImmutableArray();

        var auxReference = auxEmit.Success
            ? MetadataReference.CreateFromImage(auxImage.ToArray())
            : null;

        // 2. Control: a bare host that references that library.
        var controlReferences = libraryReference is null
            ? references
            : [.. references, libraryReference];

        var controlCompilation = RunGenerator(CSharpCompilation.Create(
            HostAssemblyName,
            [
                CSharpSyntaxTree.ParseText(ProbeSource.HostGlobalUsings, ParseOptions, "GlobalUsings.cs"),
                CSharpSyntaxTree.ParseText(ProbeSource.HostEntryPoint, ParseOptions, "Program.cs")
            ],
            controlReferences,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication)
                .WithNullableContextOptions(NullableContextOptions.Enable)));

        Control = GeneratedFilesOf(controlCompilation);
        ControlErrors = ErrorsOf(controlCompilation);

        // 2b. The same reference set, but the module declared remote. The generated files are kept
        // beside the generator's own diagnostics: the host will not compile — the actions it
        // dispatches to live behind HTTP invokers this harness does not configure — so the compiler's
        // verdict on the output says nothing, while what the host *wired* is exactly the question.
        var remote = RunGeneratorKeepingDiagnostics(CSharpCompilation.Create(
            HostAssemblyName,
            [
                CSharpSyntaxTree.ParseText(ProbeSource.HostGlobalUsings, ParseOptions, "GlobalUsings.cs"),
                CSharpSyntaxTree.ParseText(ProbeSource.RemoteHostEntryPoint, ParseOptions, "Program.cs")
            ],
            controlReferences,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication)
                .WithNullableContextOptions(NullableContextOptions.Enable)));

        Remote = GeneratedFilesOf(remote.Compilation);
        RemoteHostDiagnostics = remote.Diagnostics;

        // 2c. Standalone: references both libraries, includes one. The generated files are what is
        // measured; like the remote host, this one is not expected to compile cleanly — it declares a
        // topology whose database this harness does not configure.
        MetadataReference[] withPackage = packageReference is null
            ? controlReferences
            : [.. controlReferences, packageReference];

        MetadataReference[] standaloneReferences = auxReference is null
            ? withPackage
            : [.. withPackage, auxReference];

        Standalone = GeneratedFilesOf(RunGeneratorKeepingDiagnostics(CSharpCompilation.Create(
            HostAssemblyName,
            [
                CSharpSyntaxTree.ParseText(ProbeSource.HostGlobalUsings, ParseOptions, "GlobalUsings.cs"),
                CSharpSyntaxTree.ParseText(ProbeSource.StandaloneHostEntryPoint, ParseOptions, "Program.cs")
            ],
            standaloneReferences,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication)
                .WithNullableContextOptions(NullableContextOptions.Enable))).Compilation);

        // 2d. The control for the one above: the same two libraries, and a host that declares no
        // [Include] at all — so it hosts everything it references.
        AuxHosted = GeneratedFilesOf(RunGeneratorKeepingDiagnostics(CSharpCompilation.Create(
            HostAssemblyName,
            [
                CSharpSyntaxTree.ParseText(ProbeSource.HostGlobalUsings, ParseOptions, "GlobalUsings.cs"),
                CSharpSyntaxTree.ParseText(ProbeSource.HostEntryPoint, ParseOptions, "Program.cs")
            ],
            standaloneReferences,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication)
                .WithNullableContextOptions(NullableContextOptions.Enable))).Compilation);

        ControlDiagnostics = GeneratorDiagnosticsOf(CSharpCompilation.Create(
            HostAssemblyName,
            [
                CSharpSyntaxTree.ParseText(ProbeSource.HostGlobalUsings, ParseOptions, "GlobalUsings.cs"),
                CSharpSyntaxTree.ParseText(ProbeSource.HostEntryPoint, ParseOptions, "Program.cs")
            ],
            controlReferences,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication)
                .WithNullableContextOptions(NullableContextOptions.Enable)));

        // 3b. Bare: the same packages referenced, and nothing declared anywhere. It is the shape the
        // presence-based wiring cannot tell apart from an application that asked for a capability —
        // every Pragmatic package is on the compilation because one of them brought it, and a
        // capability wired from presence is wired here too.
        Bare = GeneratedFilesOf(RunGenerator(CSharpCompilation.Create(
            HostAssemblyName,
            [
                CSharpSyntaxTree.ParseText(ProbeSource.HostGlobalUsings, ParseOptions, "GlobalUsings.cs"),
                CSharpSyntaxTree.ParseText(ProbeSource.HostEntryPoint, ParseOptions, "Program.cs")
            ],
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication)
                .WithNullableContextOptions(NullableContextOptions.Enable))));

        // 3. Subject: a host that declares everything itself.
        Subject = GeneratedFilesOf(RunGenerator(CSharpCompilation.Create(
            HostAssemblyName,
            [
                CSharpSyntaxTree.ParseText(ProbeSource.HostGlobalUsings, ParseOptions, "GlobalUsings.cs"),
                CSharpSyntaxTree.ParseText(ProbeSource.Declarations, ParseOptions, "Probes.cs"),
                CSharpSyntaxTree.ParseText(ProbeSource.HostEntryPoint, ParseOptions, "Program.cs")
            ],
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication)
                .WithNullableContextOptions(NullableContextOptions.Enable))));
    }

    /// <summary>Compilation errors in the library after generation (empty when healthy).</summary>
    public ImmutableArray<string> LibraryErrors { get; }

    /// <summary>Errors that stopped the library assembly from being emitted (empty when healthy).</summary>
    public ImmutableArray<string> LibraryEmitErrors { get; }

    /// <summary>Generated files of the host that references the library — the control group.</summary>
    public IReadOnlyDictionary<string, string> Control { get; }

    /// <summary>
    ///     Compilation errors in the generated host (empty when healthy).
    /// </summary>
    /// <remarks>
    ///     Exposed because asserting on the text of a generated line does not prove the line compiles:
    ///     a <c>services.TryAddScoped&lt;…&gt;()</c> emitted without the extension's namespace in scope
    ///     reads exactly right and fails with CS1061. That is how it reached a real application.
    /// </remarks>
    public ImmutableArray<string> ControlErrors { get; }

    /// <summary>Generated files of the host that declares everything itself — the subject.</summary>
    public IReadOnlyDictionary<string, string> Subject { get; }

    /// <summary>
    ///     Generated files of a host that references every package and declares nothing.
    /// </summary>
    /// <remarks>
    ///     The shape that separates "this application uses the capability" from "this assembly is on
    ///     the compilation". Both the subject and the control declare things; neither can show a
    ///     capability being wired for an application that never asked for it.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Bare { get; }

    /// <summary>
    ///     Generated files of the host that reaches the module over HTTP instead of hosting it.
    /// </summary>
    /// <remarks>
    ///     Compared against <see cref="Control" />, which references the same library and hosts it.
    ///     The two differ in exactly one declaration — <c>[RemoteBoundary&lt;T&gt;]</c> — so any
    ///     difference between the generated hosts is what the topology decided.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Remote { get; }

    /// <summary>
    ///     Generated files of a host that references two modules and includes one of them.
    /// </summary>
    /// <remarks>
    ///     The shape <c>IsStandaloneHost()</c> recognises. Compared against <see cref="Control" />,
    ///     which references the same first library and includes nothing explicitly.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Standalone { get; }

    /// <summary>
    ///     Generated files of a host that references both libraries and includes neither explicitly —
    ///     so it hosts both. The control for <see cref="Standalone" />.
    /// </summary>
    public IReadOnlyDictionary<string, string> AuxHosted { get; }

    /// <summary>Errors that stopped the second library from being emitted (empty when healthy).</summary>
    public ImmutableArray<string> AuxLibraryEmitErrors { get; }

    /// <summary>Errors from emitting the package the left-out module imports.</summary>
    /// <remarks>
    ///     Asserted empty like the other two: an import that did not compile makes the module take no
    ///     package branch at all, and the case that pins the filter would pass for the wrong reason.
    /// </remarks>
    public ImmutableArray<string> PackageLibraryEmitErrors { get; }

    /// <summary>Diagnostic ids the generator raised on a host that reaches the module over HTTP.</summary>
    public ImmutableArray<string> RemoteHostDiagnostics { get; }

    /// <summary>The same, for the host that hosts the module itself — the group that must stay quiet.</summary>
    public ImmutableArray<string> ControlDiagnostics { get; }

    /// <summary>
    ///     Non-empty, trimmed lines of a generated file, or an empty list when the file was not
    ///     generated at all. Callers match whole lines: a substring match on a type name also matches
    ///     a drifted name that merely starts the same way.
    /// </summary>
    public static IReadOnlyList<string> LinesOf(IReadOnlyDictionary<string, string> files, string hintName)
        => files.TryGetValue(hintName, out var text)
            ? [.. text.Split('\n').Select(l => l.TrimEnd('\r').Trim()).Where(l => l.Length > 0)]
            : [];

    /// <summary>
    ///     The generated files of a host that references a library carrying <paramref name="declarations" />
    ///     and no translation file. <paramref name="name" /> prefixes the two assembly names.
    /// </summary>
    /// <remarks>
    ///     Built apart from the shared compilations: their probe library <em>is</em> translated
    ///     (<c>translations/en.json</c>), and a test that needs a library without translations, or with one
    ///     declaration of its own, cannot use it.
    /// </remarks>
    internal static IReadOnlyDictionary<string, string> GeneratedHostOf(string name, string declarations)
    {
        var references = HostWiringFixture.BuildReferenceSet();

        var library = CSharpCompilation.Create(
            $"{name}Library",
            [
                CSharpSyntaxTree.ParseText(ProbeSource.LibraryGlobalUsings, path: "GlobalUsings.cs"),
                CSharpSyntaxTree.ParseText(declarations, path: "Declarations.cs")
            ],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        GeneratorDriver libraryDriver = CSharpGeneratorDriver.Create(new PragmaticSourceGenerator());
        libraryDriver.RunGeneratorsAndUpdateCompilation(library, out var generatedLibrary, out _);

        using var assembly = new MemoryStream();
        var emitted = generatedLibrary.Emit(assembly);
        emitted.Success.Should().BeTrue(
            string.Join(
                "\n",
                emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));

        var host = CSharpCompilation.Create(
            $"{name}Host",
            [
                CSharpSyntaxTree.ParseText(ProbeSource.HostGlobalUsings, path: "GlobalUsings.cs"),
                CSharpSyntaxTree.ParseText(ProbeSource.HostEntryPoint, path: "Program.cs")
            ],
            references.Append(MetadataReference.CreateFromImage(assembly.ToArray())),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        GeneratorDriver hostDriver = CSharpGeneratorDriver.Create(new PragmaticSourceGenerator());
        hostDriver.RunGeneratorsAndUpdateCompilation(host, out var generatedHost, out _);

        return generatedHost.SyntaxTrees
            .Where(t => t.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
            .ToDictionary(t => Path.GetFileName(t.FilePath), t => t.GetText().ToString());
    }

    /// <summary>
    ///     One translation file, so the probe library declares i18n the way an application does.
    /// </summary>
    /// <remarks>
    ///     Translations are the one declaration in this suite that is not an attribute: they reach the
    ///     generator as <c>AdditionalFiles</c>. Without one the control group could only show the bare
    ///     host's silence, and silence on its own does not tell a gate apart from a feature that stopped
    ///     emitting altogether.
    /// </remarks>
    private sealed class TranslationFile : AdditionalText
    {
        public override string Path => "translations/en.json";

        public override Microsoft.CodeAnalysis.Text.SourceText GetText(CancellationToken cancellationToken = default)
            => Microsoft.CodeAnalysis.Text.SourceText.From("{\"probe\":{\"greeting\":\"Hello\"}}");
    }

    private static Compilation RunGenerator(CSharpCompilation compilation, bool withTranslations = false)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PragmaticSourceGenerator().AsSourceGenerator()],
            additionalTexts: withTranslations ? [new TranslationFile()] : [],
            parseOptions: ParseOptions);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return output;
    }

    /// <summary>
    ///     One generator run, keeping both halves: the compilation it produced and the ids it raised.
    /// </summary>
    private static (Compilation Compilation, ImmutableArray<string> Diagnostics) RunGeneratorKeepingDiagnostics(
        CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PragmaticSourceGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return (output, [.. diagnostics.Select(d => d.Id)]);
    }

    /// <summary>
    ///     Ids the generator itself reported, as opposed to the compiler's verdict on its output.
    /// </summary>
    private static ImmutableArray<string> GeneratorDiagnosticsOf(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PragmaticSourceGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        return [.. diagnostics.Select(d => d.Id)];
    }

    private static ImmutableArray<string> ErrorsOf(Compilation compilation)
        => [.. compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(Describe)];

    private static string Describe(Diagnostic diagnostic)
        => $"{diagnostic.Id} {diagnostic.Location.GetLineSpan().Path}({diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1}): {diagnostic.GetMessage()}";

    /// <summary>
    ///     Generated trees keyed by hint name, with the declaring assembly name normalised away.
    /// </summary>
    private static Dictionary<string, string> GeneratedFilesOf(Compilation compilation)
        => compilation.SyntaxTrees
            .Where(t => t.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
            .ToDictionary(
                t => Path.GetFileName(t.FilePath),
                t => t.GetText().ToString()
                    .Replace(LibraryAssemblyName, DeclaringAssemblyPlaceholder, StringComparison.Ordinal)
                    .Replace(HostAssemblyName, DeclaringAssemblyPlaceholder, StringComparison.Ordinal));

    /// <summary>
    ///     Every assembly the test host can see: its own output directory (the Pragmatic modules and
    ///     their package dependencies) plus the two shared frameworks.
    /// </summary>
    /// <remarks>
    ///     The generator's <c>FeatureDetector</c> decides what to emit from which assemblies the
    ///     compilation references. A hand-picked reference list would quietly switch features off and
    ///     the comparison would then pass by having measured nothing, so the set is taken wholesale.
    /// </remarks>
    internal static MetadataReference[] BuildReferenceSet()
    {
        var directories = new List<string>
        {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(typeof(Microsoft.AspNetCore.Builder.IApplicationBuilder).Assembly.Location)!,
            Path.GetDirectoryName(typeof(object).Assembly.Location)!
        };

        var byFileName = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.dll"))
            {
                var name = Path.GetFileName(file);
                if (byFileName.ContainsKey(name) || IsToolingAssembly(name))
                    continue;

                try
                {
                    // Skips native and resource-only DLLs, which carry no metadata.
                    AssemblyName.GetAssemblyName(file);
                    byFileName[name] = MetadataReference.CreateFromFile(file);
                }
                catch (BadImageFormatException) { }
                catch (FileLoadException) { }
            }
        }

        return [.. byFileName.Values];
    }

    /// <summary>
    ///     Test-host and Roslyn assemblies. They are in the output directory but belong to the runner,
    ///     not to the application under test, and referencing Roslyn twice confuses nothing but costs
    ///     time on every one of the three compilations.
    /// </summary>
    private static bool IsToolingAssembly(string fileName)
        => fileName.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase)
           || fileName.StartsWith("Microsoft.TestPlatform", StringComparison.OrdinalIgnoreCase)
           || fileName.StartsWith("Microsoft.VisualStudio.TestPlatform", StringComparison.OrdinalIgnoreCase)
           || fileName.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
           || fileName.StartsWith("testhost", StringComparison.OrdinalIgnoreCase)
           || fileName.StartsWith("Verify", StringComparison.OrdinalIgnoreCase)
           || fileName.StartsWith("Pragmatic.SourceGenerator", StringComparison.OrdinalIgnoreCase);
}
