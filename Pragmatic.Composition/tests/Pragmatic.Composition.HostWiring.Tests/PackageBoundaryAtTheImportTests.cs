using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A package's operations use the boundary the importing module names.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <c>DbContext</c> and <c>IUnitOfWork</c> are registered keyed by boundary. A package
///         declares none — that is what makes it a package — so the invoker it generates asks for them
///         <b>unkeyed</b>, and that constructor is fixed inside the package's own compilation: an
///         importer cannot key it afterwards. Six operations in
///         <c>Pragmatic.Authorization.Management</c> were in that state, and nothing had failed only
///         because no application imported the package.
///     </para>
///     <para>
///         So the key comes from the composition. The package writes into its metadata which services
///         it asks for without one; the importing module names a boundary; the generated registration
///         answers the unkeyed request from that boundary's keyed instance.
///     </para>
///     <para>
///         This needs two compilations — the package is only a package once it is an <b>assembly</b>
///         another one references — which is why none of the unit harnesses can reach the
///         <c>[UsePackage]</c> fusion.
///     </para>
/// </remarks>
public sealed class PackageBoundaryAtTheImportTests
{
    private const string PackageSource = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition;
        using Pragmatic.Result;

        namespace Probe.Package;

        public sealed class ProbePackage : IPackageDefinition
        {
            public static string PackageName => "Probe";
            public static string? RoutePrefix => "probe";
            public static string? Description => "A package whose operation needs a database";
        }

        [DomainAction]
        public partial class WriteThing : DomainAction<Guid>
        {
            private DbContext _dbContext = null!;

            public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<Guid, IError>.Success(Guid.Empty));
        }
        """;

    /// <summary>Named at the import, the boundary answers what the package could not key.</summary>
    [Fact]
    public void WithABoundaryNamed_TheImporterAnswersTheUnkeyedRequest()
    {
        var consumer = Consume("[UsePackage<global::Probe.Package.ProbePackage, ProbeBoundary>]");

        consumer.GeneratorDiagnostics.Should().NotContain("PRAG0449");

        consumer.Local.Should().NotBeNull(consumer.Files);
        consumer.Local!.Should().Contain("GetRequiredKeyedService<global::Microsoft.EntityFrameworkCore.DbContext>")
            .And.Contain("typeof(global::Probe.Consumer.ProbeBoundary)");
    }

    /// <summary>
    ///     The control: imported without one, it is refused at compile time.
    /// </summary>
    /// <remarks>
    ///     Not a fallback to the host's root context. That would be a guess about which database the
    ///     package writes to, made silently, and wrong in exactly the deployments with more than one.
    /// </remarks>
    [Fact]
    public void WithNoBoundaryNamed_TheImportIsRefused()
    {
        var consumer = Consume("[UsePackage<global::Probe.Package.ProbePackage>]");

        consumer.GeneratorDiagnostics.Should().Contain("PRAG0449");
        consumer.Local?.Should().NotContain("GetRequiredKeyedService<global::Microsoft.EntityFrameworkCore.DbContext>");
    }

    /// <summary>
    ///     The second control: a package that needs nothing keyed is imported without a boundary and
    ///     nothing is emitted or reported.
    /// </summary>
    /// <remarks>
    ///     Without it, "an import must name a boundary" would be true of every package, and both of the
    ///     packages this framework ships would have to name one for a service they never ask for.
    /// </remarks>
    [Fact]
    public void APackageThatNeedsNothingKeyed_NeedsNoBoundary()
    {
        var package = PackageSource
            .Replace("private DbContext _dbContext = null!;", "", StringComparison.Ordinal)
            .Replace("using Microsoft.EntityFrameworkCore;", "", StringComparison.Ordinal);

        var consumer = Consume("[UsePackage<global::Probe.Package.ProbePackage>]", package);

        consumer.GeneratorDiagnostics.Should().NotContain("PRAG0449");
        consumer.Local?.Should().NotContain("GetRequiredKeyedService<global::Microsoft.EntityFrameworkCore.DbContext>");
    }

    /// <summary>Two imports naming different boundaries cannot both be honoured.</summary>
    /// <remarks>
    ///     The bridge is one unkeyed registration per container: the second would shadow the first, and
    ///     one package would read the other's database with nothing to show for it. ⚠️ Two package
    ///     <b>assemblies</b>, because the boundary is recorded per package assembly — two definitions in
    ///     one assembly are one package as far as an importer can tell.
    /// </remarks>
    [Fact]
    public void TwoImportsNamingDifferentBoundaries_AreRefused()
    {
        var second = PackageSource
            .Replace("namespace Probe.Package;", "namespace Probe.Second;", StringComparison.Ordinal)
            .Replace("ProbePackage", "SecondPackage", StringComparison.Ordinal)
            .Replace("WriteThing", "WriteOther", StringComparison.Ordinal);

        var consumer = Consume(
            "[UsePackage<global::Probe.Package.ProbePackage, ProbeBoundary>]\n"
            + "[UsePackage<global::Probe.Second.SecondPackage, OtherBoundary>]",
            secondPackageSource: second,
            extraBoundary: true);

        consumer.GeneratorDiagnostics.Should().Contain("PRAG0450");
    }

    private static ConsumerResult Consume(
        string imports,
        string? packageSource = null,
        string? secondPackageSource = null,
        bool extraBoundary = false)
    {
        var packageReferences = new List<MetadataReference>
        {
            CompilePackage(packageSource ?? PackageSource, "ProbePackageAssembly")
        };

        if (secondPackageSource is not null)
            packageReferences.Add(CompilePackage(secondPackageSource, "SecondPackageAssembly"));

        var source = $$"""
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Composition.Attributes;

            namespace Probe.Consumer;

            [Boundary]
            public partial class ProbeBoundary { }

            {{(extraBoundary ? "[Boundary]\npublic partial class OtherBoundary { }\n" : "")}}
            [Module(Name = "Probe.Consumer", Version = "1.0.0")]
            {{imports}}
            public sealed class ConsumerModule;
            """;

        var compilation = CSharpCompilation.Create(
            "ProbeConsumer",
            [CSharpSyntaxTree.ParseText(source, ParseOptions, "Consumer.cs")],
            [.. References, .. packageReferences],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PragmaticSourceGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        _ = driver;

        var local = output.SyntaxTrees
            .FirstOrDefault(t => Path.GetFileName(t.FilePath).Contains("ProbeBoundary.Local", StringComparison.Ordinal))
            ?.GetText().ToString();

        return new ConsumerResult(
            [.. diagnostics.Select(d => d.Id)],
            local,
            string.Join(", ", output.SyntaxTrees.Select(t => Path.GetFileName(t.FilePath)).Where(n => n.EndsWith(".g.cs"))));
    }

    /// <summary>
    ///     Emits the package as a real assembly — the metadata an importer reads only exists on one.
    /// </summary>
    private static MetadataReference CompilePackage(string source, string assemblyName)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, ParseOptions, "Package.cs")],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PragmaticSourceGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _);

        using var image = new MemoryStream();
        var emit = generated.Emit(image);
        emit.Success.Should().BeTrue(
            "the probe package must compile, or the consumer reads no metadata and every case below "
            + "would pass for the wrong reason:\n"
            + string.Join("\n", emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Take(10)
                .Select(d => $"  - {d.Id}: {d.GetMessage()}")));

        image.Position = 0;
        return MetadataReference.CreateFromStream(image);
    }

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private static readonly ImmutableArray<MetadataReference> References = BuildReferenceSet();

    private static ImmutableArray<MetadataReference> BuildReferenceSet()
    {
        var directories = new List<string>
        {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(typeof(Microsoft.AspNetCore.Builder.IApplicationBuilder).Assembly.Location)!,
            Path.GetDirectoryName(typeof(object).Assembly.Location)!
        };

        var byFileName = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in directories.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.dll"))
            {
                var name = Path.GetFileName(file);
                if (byFileName.ContainsKey(name) || IsToolingAssembly(name))
                    continue;

                try
                {
                    // Skips native and resource-only DLLs, which carry no metadata.
                    System.Reflection.AssemblyName.GetAssemblyName(file);
                    byFileName[name] = MetadataReference.CreateFromFile(file);
                }
                catch (BadImageFormatException) { }
                catch (FileLoadException) { }
            }
        }

        return [.. byFileName.Values];
    }

    /// <summary>Runner and Roslyn assemblies: in the output directory, not in the application.</summary>
    private static bool IsToolingAssembly(string fileName)
        => fileName.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase)
           || fileName.StartsWith("Microsoft.TestPlatform", StringComparison.OrdinalIgnoreCase)
           || fileName.StartsWith("Microsoft.VisualStudio.TestPlatform", StringComparison.OrdinalIgnoreCase)
           || fileName.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
           || fileName.StartsWith("testhost", StringComparison.OrdinalIgnoreCase)
           || fileName.StartsWith("Verify", StringComparison.OrdinalIgnoreCase)
           || fileName.StartsWith("Pragmatic.SourceGenerator", StringComparison.OrdinalIgnoreCase);

    private sealed record ConsumerResult(ImmutableArray<string> GeneratorDiagnostics, string? Local, string Files);
}
