using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     Pragmatic.Authorization.Management, imported into a boundary, reads its rows through the store of that boundary's engine: the
///     boundary's generated registration calls the engine's registration of the store and answers the package's unkeyed requests with it.
/// </summary>
/// <remarks>
///     <para>
///         The package references no engine, so a module on any of them can import it; which store it gets is the importer's to say, by the
///         engine of the boundary it names. On the Sql and documents engines there is no <c>DbContext</c> to bridge, and a module there
///         need not reference EF Core at all.
///     </para>
///     <para>
///         The real package and its stores, compiled: the operations' metadata an importer reads exists only on the assembly. Each case
///         compiles the consumer's generated code too, so a registration that names a method that is not there fails here.
///     </para>
/// </remarks>
public sealed class APackagesStoreIsTheImportingBoundarysEnginesTests
{
    private const string Package = "Pragmatic.Authorization.Management";

    [Fact]
    public void On_the_Sql_engine_the_store_is_the_Sql_one_and_there_is_no_context_to_bridge()
    {
        var consumer = Consume("Sql", Package, Package + ".Engines", "Pragmatic.Persistence.Sql");

        consumer.Diagnostics.Should().NotContain("PRAG0469");
        consumer.Errors.Should().BeEmpty();
        consumer.Local.Should().NotBeNull(consumer.Files);
        consumer.Local!.Should().Contain("SqlAuthorizationManagementExtensions.AddSqlAuthorizationManagement<global::Probe.Consumer.ProbeBoundary>(services);")
            .And.Contain("GetRequiredKeyedService<global::Pragmatic.Authorization.Management.Stores.IAuthorizationManagementStore>")
            .And.NotContain("Microsoft.EntityFrameworkCore.DbContext");
    }

    [Fact]
    public void On_EF_Core_the_store_is_EF_Cores_over_the_boundarys_context()
    {
        var consumer = Consume("EfCore", Package, Package + ".EFCore");

        consumer.Diagnostics.Should().NotContain("PRAG0469");
        consumer.Errors.Should().BeEmpty();
        consumer.Local.Should().NotBeNull(consumer.Files);
        consumer.Local!.Should().Contain("AuthorizationManagementEfCoreExtensions.AddEfAuthorizationManagement<global::Probe.Consumer.ProbeBoundary>(services);")
            .And.Contain("GetRequiredKeyedService<global::Microsoft.EntityFrameworkCore.DbContext>");
    }

    /// <summary>
    ///     The control on each engine: without the package of the boundary's engine, the import is refused at compile time and names it,
    ///     rather than the application failing at start on a store nothing registered. On EF Core this is what an application that imported
    ///     the package before it named no engine meets: the EF Core part is a package of its own now.
    /// </summary>
    [Theory]
    [InlineData("Sql", "Pragmatic.Authorization.Management.Engines")]
    [InlineData("EfCore", "Pragmatic.Authorization.Management.EFCore")]
    public void Without_the_store_of_the_boundarys_engine_the_import_is_refused(string engine, string missing)
    {
        var consumer = engine == "Sql" ? Consume(engine, Package, "Pragmatic.Persistence.Sql") : Consume(engine, Package);

        consumer.Diagnostics.Should().Contain("PRAG0469");
        consumer.Messages.Should().Contain(m => m.Contains(missing, StringComparison.Ordinal));
        consumer.Local?.Should().NotContain("AuthorizationManagement<");
    }

    private static ConsumerResult Consume(string engine, params string[] assemblies)
    {
        var source = $$"""
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Probe.Consumer;

            [Boundary]
            [PersistenceEngine(PersistenceEngineKind.{{engine}})]
            public partial class ProbeBoundary { }

            [Module(Name = "Probe.Consumer", Version = "1.0.0")]
            [UsePackage<global::Pragmatic.Authorization.Management.AuthorizationManagementPackage, ProbeBoundary>]
            public sealed class ConsumerModule;
            """;

        var compilation = CSharpCompilation.Create(
            "ProbeConsumer",
            [CSharpSyntaxTree.ParseText(source, ParseOptions, "Consumer.cs")],
            [.. HostWiringFixture.BuildReferenceSet(), .. assemblies.Select(HostWiringFixture.EngineBridge)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable));

        CSharpGeneratorDriver.Create([new PragmaticSourceGenerator().AsSourceGenerator()], parseOptions: ParseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var local = output.SyntaxTrees
            .FirstOrDefault(t => Path.GetFileName(t.FilePath).Contains("ProbeBoundary.Local", StringComparison.Ordinal))
            ?.GetText().ToString();

        return new ConsumerResult(
            [.. diagnostics.Select(d => d.Id)],
            [.. diagnostics.Select(d => d.GetMessage())],
            [.. output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id}: {d.GetMessage()}")],
            local,
            string.Join(", ", output.SyntaxTrees.Select(t => Path.GetFileName(t.FilePath)).Where(n => n.EndsWith(".g.cs", StringComparison.Ordinal))));
    }

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private sealed record ConsumerResult(
        ImmutableArray<string> Diagnostics, ImmutableArray<string> Messages, ImmutableArray<string> Errors, string? Local, string Files);
}
