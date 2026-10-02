using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Mapping;

/// <summary>
///     A DTO that flattens a <c>[ValueObject]</c> into its members compiles: the generated mapping names a
///     non-nullable source as it is declared, instead of reading it through <c>?.</c> and handing a
///     possibly-null value to a non-nullable property.
/// </summary>
/// <remarks>
///     Found in the Invoicing example: <c>AddressStreet</c>, <c>AddressPostCode</c>,
///     <c>AddressCity</c> and <c>AddressCountry</c> off a non-nullable <c>PostalAddress</c> were sixteen
///     <b>CS8601</b> across two DTOs. Every Pragmatic project builds with warnings as errors — the gate's
///     clean build and the scaffold template both do — so a flattened value object was a build failure.
///     <para>
///         The source property is not nullable, so the <c>?.</c> was defending against a state the model
///         does not have, and then not producing a value the target could hold. A nullable source keeps its
///         guard: see the control below.
///     </para>
///     <para>
///         ⚠️ The condition is <b>where the value lives</b>, not the annotation. A value object sits in the
///         owner's row and is materialised with it; a <b>navigation</b> is another row, which can be
///         left-joined away or simply not <c>Include</c>d, so there the guard stays whatever the navigation
///         claims — pinned by <c>FlatteningAValueThroughADeclaredNavigationTests</c> in
///         <c>Pragmatic.Mapping.Tests</c>, which is what a first, blanket version of this fix
///         turned red.
///     </para>
/// </remarks>
public class AFlattenedValueObjectTests
{
    private const string Address = """
        [ValueObject]
        public partial record PostalAddress
        {
            public PostalAddress(string street, string city) { Street = street; City = city; }
            public string Street { get; init; } = "";
            public string City { get; init; } = "";
            private static PostalAddress Validate(string street, string city) => new(street, city);
        }
        """;

    [Fact]
    public void ANonNullableValueObject_IsNamedAsDeclared()
    {
        var mapping = GeneratorTestHelper.GetGeneratedSource(RunFlattened(), "CustomerDto.Mapping");

        mapping.Should().Contain("AddressStreet = entity.Address.Street",
            "the property is declared non-nullable, so the generated code says so");
        mapping.Should().NotContain("entity.Address?.Street",
            "a guard against a state the model does not have, handing a string? to a string");
    }

    /// <summary>
    ///     The measurement the issue asks for: the generated files carry no <c>CS8601</c>.
    /// </summary>
    [Fact]
    public void TheGeneratedMapping_AssignsNothingPossiblyNullToANonNullableMember()
    {
        var offenders = NullabilityWarningsInGeneratedCode(RunFlattened());

        offenders.Should().BeEmpty(
            "each flattened member was a CS8601, and warnings are errors in every Pragmatic project: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    ///     The control: a <b>nullable</b> value object keeps its <c>?.</c> — there the guard is right, and
    ///     the DTO says the member is nullable too, so nothing is assigned that cannot be held.
    /// </summary>
    [Fact]
    public void ANullableValueObject_KeepsItsGuard()
    {
        var result = Run($$"""
            {{Address}}

            [Entity]
            [BelongsTo<ThingBoundary>]
            public partial class Supplier : IEntity
            {
                public string Name { get; private set; } = "";
                public PostalAddress? Address { get; private set; }
            }

            [MapFrom<Supplier>]
            [GenerateProjection]
            public partial class SupplierDto
            {
                public string Name { get; init; } = "";
                public string? AddressStreet { get; init; }
                public string? AddressCity { get; init; }
            }
            """);

        GeneratorTestHelper.GetGeneratedSource(result, "SupplierDto.Mapping")
            .Should().Contain("AddressStreet = entity.Address?.Street",
                "the source may be null, and the target says it may be");

        NullabilityWarningsInGeneratedCode(result).Should().BeEmpty();
    }

    /// <summary>
    ///     And <c>Money</c>: the persistence generator maps it as a
    ///     complex type too, so it lives in the owner's row and reads through <c>.</c> like any other
    ///     value object.
    /// </summary>
    /// <remarks>
    ///     On a <c>struct</c> the <c>?.</c> shape is not even a warning: <c>?.</c> on a non-nullable value
    ///     type is <b>CS0023</b>, so a DTO that flattened an amount through it would not compile at all.
    /// </remarks>
    [Fact]
    public void AFlattenedMoney_ReadsThroughTheValueToo()
    {
        var result = Run("""
            [Entity]
            [BelongsTo<ThingBoundary>]
            public partial class Payment : IEntity
            {
                public Money Amount { get; private set; }
            }

            [MapFrom<Payment>]
            [GenerateProjection]
            public partial class PaymentAmountDto
            {
                public decimal AmountAmount { get; init; }
            }
            """);

        var mapping = GeneratorTestHelper.GetGeneratedSource(result, "PaymentAmountDto.Mapping");

        mapping.Should().Contain("AmountAmount = entity.Amount.Amount");
        mapping.Should().NotContain("is not loaded on this",
            "a complex type arrives with the row, and `is null` on a struct does not compile");
        mapping.Should().NotContain("RequiredNavigations { get; } = [\"Amount\"]",
            "Include on a complex type is not something EF can be asked for");

        // ⚠️ Not "the whole thing compiles": this stand-in compilation has no EF Core, no Specification
        // and no Abstractions, so the repository and spec files the persistence generator writes cannot
        // resolve their own types. That is the harness's reference closure, not this mapping — which is
        // why the assertions above name what the mapping says. The nullability check is id-scoped, so
        // the missing references cannot make it pass.
        NullabilityWarningsInGeneratedCode(result).Should().BeEmpty();
    }

    private static SourceGenRunResult RunFlattened()
        => Run($$"""
            {{Address}}

            [Entity]
            [BelongsTo<ThingBoundary>]
            public partial class Customer : IEntity
            {
                public string Name { get; private set; } = "";
                public PostalAddress Address { get; private set; } = new("", "");
            }

            [MapFrom<Customer>]
            [GenerateProjection]
            public partial class CustomerDto
            {
                public string Name { get; init; } = "";
                public string AddressStreet { get; init; } = "";
                public string AddressCity { get; init; } = "";
            }
            """);

    /// <summary>
    ///     Every "possible null assigned to a non-nullable member" the output compilation reports inside a
    ///     generated file, as "file(line): id".
    /// </summary>
    private static string[] NullabilityWarningsInGeneratedCode(SourceGenRunResult result)
    {
        var generated = result.GeneratedTrees.Select(t => t.FilePath).ToHashSet(System.StringComparer.Ordinal);

        return result.OutputCompilation.GetDiagnostics()
            .Where(d => d.Id is "CS8601" or "CS8600" or "CS8602" or "CS8604")
            .Where(d => generated.Contains(d.Location.SourceTree?.FilePath ?? string.Empty))
            .Select(d => $"{System.IO.Path.GetFileName(d.Location.SourceTree?.FilePath)}"
                         + $"({d.Location.GetLineSpan().StartLinePosition.Line + 1}): {d.Id}")
            .ToArray();
    }

    /// <remarks>
    ///     <c>Money</c> is declared here rather than referenced: the generator recognises it by its full
    ///     name, and this test project does not reference Internationalization — the same stand-in
    ///     <c>MoneyProjectionTests</c> uses, for the same reason.
    /// </remarks>
    private static SourceGenRunResult Run(string declarations)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            $$"""
            using System;
            using System.Collections.Generic;

            namespace Pragmatic.Internationalization.Types
            {
                public readonly struct Money { public decimal Amount { get; init; } }
            }

            namespace Projections
            {
                using Pragmatic.Internationalization.Types;
                using Pragmatic.Persistence.Entity;
                using Pragmatic.Mapping.Attributes;
                using Pragmatic.Persistence.Query.Attributes;

                public sealed class ThingBoundary;

                {{declarations}}
            }
            """,
            GeneratorTestHelper.FromType<EntityAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Mapping.Attributes.MapFromAttribute<object>>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(global::Pragmatic.Endpoints.Processors.IEndpointPreProcessor)));
}
