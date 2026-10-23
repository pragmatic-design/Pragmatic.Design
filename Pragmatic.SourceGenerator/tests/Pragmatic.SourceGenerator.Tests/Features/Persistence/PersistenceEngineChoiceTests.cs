using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Mutation;
using Pragmatic.Composition.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     Which engine persists an entity: the one its boundary names with <c>[PersistenceEngine]</c>, else the only
///     engine the compilation references.
/// </summary>
/// <remarks>
///     The Sql engine's bridge is not in the repository yet, so a test that needs two engines referenced declares the
///     type the generator recognises it by. That type is all the generator reads to decide the engine is there.
/// </remarks>
public class PersistenceEngineChoiceTests
{
    private const string SqlBridge = """
        namespace Pragmatic.Persistence.Sql.Pragmatic
        {
            public static class PragmaticSqlFilters { }
        }
        """;

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<ModuleAttribute>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Mutation<>)),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>()
    ];

    private static SourceGenRunResult Run(string body, bool withSqlBridge)
        => GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(Source(body, withSqlBridge), References);

    private static string Source(string body, bool withSqlBridge) => $$"""
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Contoso.Billing
        {
            {{body}}

            public static class Program { public static void Main() { } }
        }

        {{(withSqlBridge ? SqlBridge : "")}}
        """;

    private static IReadOnlyList<Diagnostic> EngineDiagnostics(SourceGenRunResult result)
        => [.. GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0743"), .. GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0744")];

    [Fact]
    public void OneEngineReferenced_IsTheDefault_AndNothingIsReported()
    {
        var result = Run("""
            [Boundary]
            public partial class BillingBoundary;

            [Entity]
            public partial class Invoice : IEntity { public string Number { get; private set; } = ""; }
            """, withSqlBridge: false);

        EngineDiagnostics(result).Should().BeEmpty("with one engine referenced there is nothing to choose");
        GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("DbContext.Billing") && kv.Value.Contains("DbSet<Contoso.Billing.Invoice>"))
            .Should().NotBeEmpty("the one engine referenced persists the entity, as before the marker existed");
    }

    [Fact]
    public void TwoEnginesReferenced_AnEntityWhoseBoundaryNamesNone_IsPrag0743_Once()
    {
        var result = Run("""
            [Boundary]
            public partial class BillingBoundary;

            [Entity]
            public partial class Invoice : IEntity { public string Number { get; private set; } = ""; }
            """, withSqlBridge: true);

        var error = GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0743").Should().ContainSingle(
            "the entity's boundary (the assembly's only one) names no engine, and there are two to choose from").Which;
        error.GetMessage().Should().Contain("Invoice").And.Contain("Contoso.Billing.BillingBoundary");
        error.Severity.Should().Be(DiagnosticSeverity.Error);
    }

    /// <summary>The control for the one above: the same compilation, the boundary naming its engine.</summary>
    [Fact]
    public void TwoEnginesReferenced_TheSingleBoundaryNamesOne_IsSilent()
    {
        var result = Run("""
            [Boundary]
            [PersistenceEngine(PersistenceEngineKind.Sql)]
            public partial class BillingBoundary;

            [Entity]
            public partial class Invoice : IEntity { public string Number { get; private set; } = ""; }
            """, withSqlBridge: true);

        EngineDiagnostics(result).Should().BeEmpty();
    }

    /// <summary>Two boundaries, one reached through [BelongsTo], the other through [Owns]: each marker is read.</summary>
    [Fact]
    public void TwoEnginesReferenced_TheMarkerIsReadThroughBelongsToAndOwns()
    {
        var result = Run("""
            [Boundary]
            [PersistenceEngine(PersistenceEngineKind.Sql)]
            public partial class CatalogBoundary;

            [Boundary]
            [Owns<Invoice>]
            [PersistenceEngine(PersistenceEngineKind.EfCore)]
            public partial class BillingBoundary;

            [Entity]
            [BelongsTo<CatalogBoundary>]
            public partial class Product : IEntity { public string Name { get; private set; } = ""; }

            [Entity]
            public partial class Invoice : IEntity { public string Number { get; private set; } = ""; }

            [Entity]
            [BelongsTo<UnmarkedBoundary>]
            public partial class Note : IEntity { public string Text { get; private set; } = ""; }

            [Boundary]
            [Owns<Note>]
            public partial class UnmarkedBoundary;
            """, withSqlBridge: true);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0743").Should().ContainSingle(
                "Product's and Invoice's boundaries name their engines; only Note's names none")
            .Which.GetMessage().Should().Contain("Note");
        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0744").Should().BeEmpty();
    }

    [Fact]
    public void AMarkerNamingAnEngineThatIsNotReferenced_IsPrag0744()
    {
        const string body = """
            [Boundary]
            [PersistenceEngine(PersistenceEngineKind.Documents)]
            public partial class BillingBoundary;

            [Entity]
            public partial class Invoice : IEntity { public string Number { get; private set; } = ""; }
            """;
        var result = Run(body, withSqlBridge: false);

        var error = GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0744").Should().ContainSingle().Which;
        error.GetMessage().Should().Contain("Contoso.Billing.BillingBoundary").And.Contain("Documents");
        var lines = Source(body, withSqlBridge: false).Split('\n');
        lines[error.Location.GetLineSpan().StartLinePosition.Line].Should().Contain("class BillingBoundary",
            "the fix is on the boundary, so that is where it is reported");
    }
}
