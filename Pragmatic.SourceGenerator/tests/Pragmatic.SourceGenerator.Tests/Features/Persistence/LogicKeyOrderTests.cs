using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     Where each part of a composite <c>[LogicKey]</c> sits, and what decides it.
/// </summary>
/// <remarks>
///     <para>
///         Two things read the order and neither is cosmetic: the unique index, whose first column is
///         the only one it can be searched by alone, and the generated
///         <c>GetBy{A}And{B}Async(a, b)</c>, where two strings in the wrong order compile and look up
///         the wrong row.
///     </para>
///     <para>
///         The default has to stay declaration order — every entity that predates <c>Order</c> has an
///         index built from it, and a sort that moved them would be a migration nobody asked for. That
///         is what the first test holds down.
///     </para>
///     <para>
///         Asserted here on the repository lookup, which a module compilation emits. The index built
///         from the same order lives in the host — <c>WithOrderedLogicKey</c> in
///         <see cref="GeneratedSurfaceInventoryTests"/> covers it, compiled and in the snapshot.
///     </para>
/// </remarks>
public class LogicKeyOrderTests
{
    [Fact]
    public void CompositeLogicKey_WithoutOrder_KeepsDeclarationOrder()
    {
        var generated = Run("""
            [LogicKey]
            public string CountryCode { get; private set; } = "";
            [LogicKey]
            public string Vat { get; private set; } = "";
            """);

        generated.Should().Contain("GetByCountryCodeAndVatAsync");
        generated.Should().Contain("e.CountryCode == countryCode && e.Vat == vat");
    }

    [Fact]
    public void CompositeLogicKey_WithOrder_FollowsItRatherThanTheDeclaration()
    {
        var generated = Run("""
            [LogicKey(Order = 2)]
            public string CountryCode { get; private set; } = "";
            [LogicKey(Order = 1)]
            public string Vat { get; private set; } = "";
            """);

        generated.Should().Contain("GetByVatAndCountryCodeAsync");
        generated.Should().NotContain("GetByCountryCodeAndVatAsync");
    }

    [Fact]
    public void CompositeLogicKey_OrderOnOnePartOnly_TreatsTheOtherAsZero()
    {
        var generated = Run("""
            [LogicKey(Order = 1)]
            public string CountryCode { get; private set; } = "";
            [LogicKey]
            public string Vat { get; private set; } = "";
            """);

        generated.Should().Contain("GetByVatAndCountryCodeAsync",
            "an unset Order is 0 and sorts ahead of 1 — setting it on one part of two is a way to be "
            + "wrong quietly, so the behaviour is pinned rather than left to be discovered");
    }

    private static string Run(string logicKeyProperties)
    {
        // Two references, so the generated repository names EF Core and Specification types nothing
        // here resolves. That the generated code compiles is GeneratedSurfaceInventoryTests' signal,
        // not this one's; what is read here is the order the transform put the parts in.
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            $$"""
            using System;
            using Pragmatic.Persistence.Entity;

            namespace Keys;

            public sealed class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Company : IEntity
            {
                {{logicKeyProperties}}
            }
            """,
            GeneratorTestHelper.FromType<EntityAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>());

        return string.Join(
            "\n",
            GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Values);
    }
}
