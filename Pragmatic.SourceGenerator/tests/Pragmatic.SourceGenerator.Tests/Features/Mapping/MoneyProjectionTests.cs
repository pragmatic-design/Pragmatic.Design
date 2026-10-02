using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Mapping;

/// <summary>
///     A <c>Money</c> property survives the projection, exactly as a <c>[ValueObject]</c> does.
/// </summary>
/// <remarks>
///     <para>
///         Same trap, same shape as <see cref="ValueObjectProjectionTests" />: the persistence generator
///         maps <c>Money</c> as an EF Core complex type — <c>Amount_Amount</c> and <c>Amount_Currency</c> —
///         but the type carries no <c>[ValueObject]</c> attribute, so the projection's list of what SQL can
///         translate must name it explicitly. Otherwise the property is dropped from the expression without
///         a diagnostic, and every projected read answers <c>0</c> while the rows hold the money.
///     </para>
///     <para>
///         In the Invoicing example that is two payments of 100,00 and 200,00 coming back from
///         <c>GET api/invoices/{id}/payments</c> as "0 0", while <c>FromEntity</c> — which is a
///         different path — answers correctly.
///     </para>
/// </remarks>
public class MoneyProjectionTests
{
    [Fact]
    public void AMoneyProperty_IsCarriedByTheProjection()
    {
        var result = Run("""
            [Entity]
            [BelongsTo<ThingBoundary>]
            public partial class Payment : IEntity
            {
                public string Reference { get; private set; } = "";
                public Money Amount { get; private set; }
            }

            [MapFrom<Payment>]
            [GenerateProjection]
            public partial class PaymentDto
            {
                public string Reference { get; init; } = "";
                public Money Amount { get; init; }
            }
            """);

        var mapping = GeneratorTestHelper.GetGeneratedSource(result, "PaymentDto.Mapping");

        mapping.Should().Contain("Amount = entity.Amount",
            "EF projects a complex type whole, so leaving it out returns zero while the columns hold the money");
        mapping.Should().Contain("Reference = entity.Reference",
            "the control: the rest of the projection is unchanged");
    }

    /// <summary>
    ///     And inside an inlined child collection, which is the path a read of an invoice with its lines
    ///     takes.
    /// </summary>
    /// <remarks>
    ///     Two filters, not one — the second is the one that inlines a collection of child DTOs, and it has
    ///     its own list of what it can carry.
    /// </remarks>
    [Fact]
    public void AMoneyInsideAChildCollection_SurvivesTheInlinedProjection()
    {
        var result = Run("""
            [Entity]
            [BelongsTo<ThingBoundary>]
            [Relation.OneToMany<InvoiceLine>]
            public partial class Invoice : IEntity
            {
                public string Number { get; private set; } = "";
            }

            [Entity]
            [BelongsTo<ThingBoundary>]
            [PartOf<Invoice>]
            public partial class InvoiceLine : IEntity
            {
                public Money LineNet { get; private set; }
            }

            [MapFrom<InvoiceLine>]
            [GenerateProjection]
            public partial class InvoiceLineDto
            {
                public Money LineNet { get; init; }
            }

            [MapFrom<Invoice>]
            [GenerateProjection]
            public partial class InvoiceDto
            {
                public string Number { get; init; } = "";
                public List<InvoiceLineDto> InvoiceLines { get; init; } = [];
            }
            """);

        GeneratorTestHelper.GetGeneratedSource(result, "InvoiceDto.Mapping")
            .Should().Contain("LineNet = x.LineNet",
                "the inlined element projection has its own filter, and a Money fell through all of its branches");
    }

    /// <remarks>
    ///     <c>Money</c> is declared here rather than referenced: the generator recognises it by its full
    ///     name, and this test project does not reference Internationalization — the same stand-in
    ///     <c>AMoneyNeedsNoConverterTests</c> uses, for the same reason.
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
