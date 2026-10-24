using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The engine a module's boundary names crosses to the host through the module's metadata, and the host checks
///     it against the engines the host references.
/// </summary>
/// <remarks>
///     <para>
///         The host is where the choice first exists. A module that references only EF Core has one engine and
///         nothing to choose; the host that also references Sql has two, and the module's entities reach it as
///         metadata, not as source. A check that read only the compilation's own entities was silent there.
///     </para>
///     <para>
///         So the module is compiled through the generator and emitted, and the host references the result. The Sql
///         bridge is not in the repository yet: a stub of the type the generator recognises it by stands in for it.
///     </para>
/// </remarks>
public class AModulesPersistenceEngineReachesTheHostTests
{
    private const string SqlBridge = """
        namespace Pragmatic.Persistence.Sql
        {
            public static class PragmaticSqlFilters { }
        }
        """;

    [Fact]
    public void AHostWithTwoEngines_ReportsAModuleEntityWhoseBoundaryNamesNone()
    {
        var result = Host(Module(marker: ""), hostReferencesSql: true);

        var error = GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0743").Should().ContainSingle(
            "the module had one engine and nothing to report; the host has two, and the boundary names neither").Which;
        error.GetMessage().Should().Contain("Billing.Invoice").And.Contain("Billing.BillingBoundary");
    }

    /// <summary>The control for the one above: the boundary names its engine, and the name reaches the host.</summary>
    [Fact]
    public void AHostWithTwoEngines_ReadsTheEngineTheModuleNamed()
    {
        var result = Host(Module(marker: "[PersistenceEngine(PersistenceEngineKind.EfCore)]"), hostReferencesSql: true);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0743").Should().BeEmpty(
            "the boundary names EF Core, and the module's metadata carries that to the host");
        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0744").Should().BeEmpty();
    }

    [Fact]
    public void AHostThatDoesNotReferenceTheEngineAModuleNamed_ReportsTheModulesBoundary()
    {
        var sqlBridge = GeneratorTestHelper.CompileReference("Pragmatic.Persistence.Sql", SqlBridge);
        var module = Module(marker: "[PersistenceEngine(PersistenceEngineKind.Sql)]", sqlBridge);

        var result = Host(module, hostReferencesSql: false);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0744").Should().ContainSingle(
                "the module compiled against the Sql bridge, and the host does not reference it")
            .Which.GetMessage().Should().Contain("Billing.BillingBoundary").And.Contain("Sql");
    }

    /// <summary>
    ///     A package declares no boundary, so its entities take the engine of the boundary that adopts them.
    /// </summary>
    /// <remarks>
    ///     The adopting module declares no entity of its own: the package's is the only one that can carry the
    ///     boundary's engine to the host.
    /// </remarks>
    [Fact]
    public void AnAdoptedPackagesEntity_TakesTheEngineOfTheBoundaryThatAdoptsIt()
    {
        var sqlBridge = GeneratorTestHelper.CompileReference("Pragmatic.Persistence.Sql", SqlBridge);
        var package = GeneratorTestHelper.RunGeneratorAsReference<PragmaticSourceGenerator>(
            "Contoso.Notes.Package", """
                using Pragmatic.Persistence.Entity;

                namespace Contoso.Notes;

                public sealed class NotesPackage : Pragmatic.Composition.IPackageDefinition
                {
                    public static string PackageName => "Notes";
                    public static string? RoutePrefix => null;
                    public static string? Description => null;
                }

                [Entity]
                public partial class Note : IEntity
                {
                    public string Text { get; private set; } = "";
                }
                """, References);
        var module = GeneratorTestHelper.RunGeneratorAsReference<PragmaticSourceGenerator>(
            "Billing.Module", """
                namespace Billing;

                [Pragmatic.Actions.Attributes.Boundary]
                [Pragmatic.Persistence.Entity.PersistenceEngine(Pragmatic.Persistence.Entity.PersistenceEngineKind.Sql)]
                public partial class BillingBoundary;

                [Pragmatic.Composition.Attributes.Module]
                [Pragmatic.Composition.Attributes.UsePackage<Contoso.Notes.NotesPackage, BillingBoundary>]
                public partial class BillingModule;
                """, [.. References, package, sqlBridge]);

        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>("""
            namespace AppHost
            {
                public static class Program { public static void Main() { } }
            }
            """, [.. References, package, module]);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0744").Should().ContainSingle(
                "the package's entity is adopted by a boundary that names Sql, which the host does not reference")
            .Which.GetMessage().Should().Contain("Billing.BillingBoundary");
    }

    private static MetadataReference Module(string marker, params MetadataReference[] extra)
        => GeneratorTestHelper.RunGeneratorAsReference<PragmaticSourceGenerator>(
            "Billing.Module", $$"""
                using Pragmatic.Actions.Attributes;
                using Pragmatic.Persistence.Entity;

                namespace Billing;

                [Boundary]
                {{marker}}
                public partial class BillingBoundary;

                [Entity]
                public partial class Invoice : IEntity
                {
                    public string Number { get; private set; } = "";
                }
                """, [.. References, .. extra]);

    private static SourceGenRunResult Host(MetadataReference module, bool hostReferencesSql)
        => GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>($$"""
            {{(hostReferencesSql ? SqlBridge : "")}}

            namespace AppHost
            {
                public static class Program { public static void Main() { } }
            }
            """, [.. References, module]);

    /// <summary>What the module and the host compile against; the EF Core bridge is the one engine both reference.</summary>
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.BoundaryAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.MultiTenancy.ITenantEntity>(),
        .. ByName(
            "System.Text.Json",
            "System.ComponentModel.TypeConverter",
            "System.Linq.Queryable",
            "System.ComponentModel.Annotations",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Relational",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Logging.Abstractions",
            "Pragmatic.Result",
            "Pragmatic.Ensure",
            "Pragmatic.Specification",
            "Pragmatic.Mapping",
            "Pragmatic.Mapping.EFCore",
            "Pragmatic.Validation",
            "Pragmatic.Actions"),
    ];

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException(
                $"'{n}' does not resolve in the test process. Add the project reference, or the "
                + "compilation silently loses whatever needs it."));
}
