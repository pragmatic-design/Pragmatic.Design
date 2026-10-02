using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>PRAG0651</c> is for a type EF Core cannot map; a <c>Money</c> is mapped by the configuration this
///     generator writes — <c>RenderMoneyComplexProperty</c>, an EF Core complex type.
/// </summary>
/// <remarks>
///     Same shape as <see cref="ALocalizedStringNeedsNoConverterTests" /> and the same defect: the warning
///     exempted the kinds of property the generator maps itself and not this one. An entity could
///     therefore not hold an amount of money at all in a project that treats warnings as errors, which is
///     every Pragmatic project — the first application to bill anybody met seven of them at once, and the
///     skill that tells you to use <c>Money</c> instead of <c>decimal</c> could not be followed.
/// </remarks>
public class AMoneyNeedsNoConverterTests
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
                public readonly struct Money { public decimal Amount { get; init; } }
            }

            namespace Contoso.Billing
            {
                [Boundary]
                public partial class BillingBoundary;

                public sealed class Swatch { public string Hex { get; set; } = ""; }
            }

            namespace Contoso.Billing.Entities
            {
                [Entity]
                public partial class Invoice : IEntity
                {
                    public {{propertyType}} Total { get; private set; } = null!;
                }
            }
            """, References);

    [Fact]
    public void AMoneyProperty_IsNotReported()
    {
        var result = Run("Pragmatic.Internationalization.Types.Money");

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0651").Should().BeFalse(
            "the entity configuration maps a Money as an EF Core complex type itself");
    }

    /// <summary>The control: a class nobody maps is still what the warning is for.</summary>
    [Fact]
    public void AClassNobodyMaps_IsStillReported()
    {
        var result = Run("Contoso.Billing.Swatch");

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0651").Should().BeTrue();
    }
}
