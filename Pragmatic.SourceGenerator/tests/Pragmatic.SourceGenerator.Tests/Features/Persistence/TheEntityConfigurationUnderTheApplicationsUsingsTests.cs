using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Composition.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The generated entity configuration compiles whatever the application imports globally.
/// </summary>
/// <remarks>
///     <c>DeleteBehavior</c> is declared in <c>Pragmatic.Persistence.Entity</c> and in
///     <c>Microsoft.EntityFrameworkCore</c>. A configuration that imports the second and writes the bare
///     name holds until the application imports the first everywhere — a <c>global using</c> of
///     its own, or the implicit usings of the Pragmatic packages — and then every relationship's
///     <c>.OnDelete(DeleteBehavior.X)</c> is CS0104, in code the application did not write.
/// </remarks>
public class TheEntityConfigurationUnderTheApplicationsUsingsTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<ModuleAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        // Without EF Core the bare name has one candidate, and the ambiguity cannot arise.
        GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
        GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DeleteBehavior>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Microsoft.EntityFrameworkCore.RelationalEntityTypeBuilderExtensions))
    ];

    private static SourceGenRunResult Run() =>
        GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>("""
            global using Pragmatic.Persistence.Entity;
            using Pragmatic.Actions.Attributes;

            namespace Contoso.Billing;

            [Boundary]
            public partial class BillingBoundary;

            [Entity]
            [Relation.OneToMany<Fee>.WithNavigation("Fees", OnDelete = DeleteBehavior.Cascade)]
            public partial class Invoice : IEntity { }

            [Entity]
            public partial class Fee : IEntity { }

            public static class Program { public static void Main() { } }
            """, References);

    [Fact]
    public void ARelationshipsDeleteBehaviour_IsNotAmbiguousUnderAGlobalUsingOfTheEntityNamespace()
    {
        var result = Run();

        var configurations = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("EntityConfig."))
            .Select(kv => kv.Value);
        configurations.Should().Contain(source => source.Contains(".OnDelete("),
            "the relationship is configured, so the line that names the delete behaviour is there to compile");

        GeneratorTestHelper.GetCompilationErrors(result)
            .Where(d => d.Location.SourceTree?.FilePath.Contains("EntityConfig.") == true)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .Should().BeEmpty();
    }
}
