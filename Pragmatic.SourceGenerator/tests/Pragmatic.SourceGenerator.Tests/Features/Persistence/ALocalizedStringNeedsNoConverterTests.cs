using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>PRAG0651</c> is for a type EF Core cannot map; a <c>LocalizedString</c> is mapped
///     by the configuration this generator writes.
/// </summary>
/// <remarks>
///     The warning exempted the other kinds of property the generator maps itself and not this one, so
///     an application with content in several languages stopped on its own generated converter — and
///     the Showcase put the id in <c>NoWarn</c>.
/// </remarks>
public class ALocalizedStringNeedsNoConverterTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>()
    ];

    private static SourceGenRunResult Run(string propertyType)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Pragmatic.Internationalization.Types
            {
                public sealed class LocalizedString { }
            }

            namespace Contoso.Catalog
            {
                [Boundary]
                public partial class CatalogBoundary;

                public sealed class Swatch { public string Hex { get; set; } = ""; }
            }

            namespace Contoso.Catalog.Entities
            {
                [Entity]
                public partial class Product : IEntity
                {
                    public {{propertyType}} Label { get; private set; } = null!;
                }
            }
            """, References);

    [Fact]
    public void ALocalizedStringProperty_IsNotReported()
    {
        var result = Run("Pragmatic.Internationalization.Types.LocalizedString");

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0651").Should().BeFalse(
            "the entity configuration maps a LocalizedString to a JSON column itself");
    }

    /// <summary>The control: a class nobody maps is still what the warning is for.</summary>
    [Fact]
    public void AClassNobodyMaps_IsStillReported()
    {
        var result = Run("Contoso.Catalog.Swatch");

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0651").Should().BeTrue();
    }
}
