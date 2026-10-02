using System;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     An <c>[Invariant]</c> on a <c>[PartOf]</c> child is checked when the aggregate that carries it is
///     written — which is the only way it is ever written.
/// </summary>
/// <remarks>
///     <para>
///         Measured in the Invoicing example: <c>InvoiceLine</c> is <c>[PartOf&lt;Invoice&gt;]</c>
///         and declared its own rule about the VAT rates in use. A draft with a line at 13% was
///         <b>created</b>, 201, with its VAT computed on that rate. The rule ran on no path at all: the
///         invoker checked the invariants of the entity it wrote and nothing else, and a child of an
///         aggregate has no operations of its own by construction.
///     </para>
///     <para>
///         So the rule read as enforced and enforced nothing — "declared and never read", in the one
///         place where the reader has no way to notice.
///     </para>
/// </remarks>
public class AChildsInvariantIsCheckedTests
{
    private const string EfCorePresence = """
        namespace Pragmatic.Persistence.EFCore
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class PragmaticDbContextAttribute : Attribute { }
        }
        """;

    /// <param name="lineInvariant">What the child declares about itself.</param>
    /// <param name="orderInvariant">What the aggregate declares — the control for "still checked".</param>
    private static string Source(string lineInvariant, string orderInvariant = "") => $$"""
        using System;
        using System.Collections.Generic;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;

        {{EfCorePresence}}

        namespace TestApp;

        public class SalesBoundary;

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            public string Reference { get; private set; } = "";
            public ICollection<LineItem> Lines { get; set; } = new List<LineItem>();
            public Address? ShipTo { get; set; }
            {{orderInvariant}}
        }

        [Entity]
        [BelongsTo<SalesBoundary>]
        [PartOf<Order>]
        public partial class LineItem : IEntity
        {
            public string Description { get; set; } = "";
            public decimal VatRate { get; set; }
            {{lineInvariant}}
        }

        [Entity]
        [BelongsTo<SalesBoundary>]
        [PartOf<Order>]
        public partial class Address : IEntity
        {
            public string City { get; set; } = "";

            [Invariant("A shipping address names a city")]
            public bool NamesACity() => City.Length > 0;
        }

        [Mutation(Mode = MutationMode.Update, Internal = true)]
        public partial class WriteLineItemMutation : Mutation<LineItem>
        {
            public Guid Id { get; init; }
            public string Description { get; init; } = "";
        }

        [Mutation(Mode = MutationMode.Update, Internal = true)]
        public partial class WriteAddressMutation : Mutation<Address>
        {
            public Guid Id { get; init; }
            public string City { get; init; } = "";
        }

        [Mutation(Mode = MutationMode.Update)]
        public partial class UpdateOrderMutation : Mutation<Order>
        {
            public Guid Id { get; init; }
            public string? Reference { get; init; }
            public List<WriteLineItemMutation> Lines { get; init; } = new();
            public WriteAddressMutation? ShipTo { get; init; }
        }
        """;

    private const string LineRule = """
        [Invariant("The VAT rate must be one of 0, 4, 5, 10 or 22 per cent")]
        public bool ChargesARateInUse() => VatRate is 0m or 4m or 5m or 10m or 22m;
        """;

    [Fact]
    public void AChildOfTheAggregate_HasItsInvariantChecked()
    {
        var invoker = Invoker(Source(LineRule));

        invoker.Should().Contain("ChargesARateInUse()",
            "the invoker merged the children, so it knows which ones to ask");
        invoker.Should().Contain("entity.Lines",
            "each child of the collection is asked, not the collection");
    }

    /// <summary>The refusal names where the rule lives, because two aggregates may carry the same name.</summary>
    [Fact]
    public void TheRefusal_NamesTheChildAndTheRule()
        => Invoker(Source(LineRule)).Should()
            .Contain("\"LineItem.ChargesARateInUse\"")
            .And.Contain("The VAT rate must be one of 0, 4, 5, 10 or 22 per cent");

    /// <summary>A child that is one, not many, is asked too — and only when it is there.</summary>
    [Fact]
    public void ASingleChild_IsAskedWhenPresent()
    {
        var invoker = Invoker(Source(LineRule));

        invoker.Should().Contain("NamesACity()");
        invoker.Should().Contain("entity.ShipTo is not null",
            "a reference child may be absent, and an absent child breaks no rule");
    }

    /// <summary>The control: the aggregate's own invariants are still checked, and first.</summary>
    [Fact]
    public void TheAggregatesOwnInvariant_IsStillCheckedFirst()
    {
        var invoker = Invoker(Source(LineRule, """
            [Invariant("An order has at least one line")]
            public bool HasLines() => Lines.Count > 0;
            """));

        var own = invoker.IndexOf("entity.HasLines()", StringComparison.Ordinal);
        var child = invoker.IndexOf("ChargesARateInUse()", StringComparison.Ordinal);

        own.Should().BeGreaterThan(-1);
        child.Should().BeGreaterThan(own, "the aggregate answers for itself before it answers for its parts");
    }

    /// <summary>
    ///     The second control, the one that keeps this from becoming "walk every child every time":
    ///     a child with no rule of its own produces no loop.
    /// </summary>
    [Fact]
    public void AChildWithNoInvariant_IsNotWalked()
    {
        var invoker = Invoker(Source(lineInvariant: ""));

        invoker.Should().NotContain("entity.Lines",
            "nothing on LineItem declares a rule, so there is nothing to ask it");
    }

    /// <summary>And the generated invoker compiles.</summary>
    [Fact]
    public void TheInvoker_Compiles()
    {
        var result = Run(Source(LineRule));

        var errors = result.OutputCompilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
                        && (d.Location.SourceTree?.FilePath ?? "").Contains("UpdateOrderMutation", StringComparison.Ordinal))
            .Select(d => d.ToString())
            .ToArray();

        errors.Should().BeEmpty(string.Join(" | ", errors));
    }

    private static string Invoker(string source)
        => GeneratorTestHelper.GetGeneratedSource(Run(source), "UpdateOrderMutation.MutationInvoker")
           ?? throw new InvalidOperationException("no invoker was generated");

    private static SourceGenRunResult Run(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source,
            [
                GeneratorTestHelper.FromType<Pragmatic.Actions.Attributes.DomainActionAttribute>(),
                GeneratorTestHelper.FromType<Pragmatic.Result.IError>(),
                GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Result<,>)),
                GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Ensure.Ensure)),
                GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.IEntity>(),
                GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.SoftDeleteAttribute>(),
                GeneratorTestHelper.FromType<Pragmatic.Mapping.Attributes.MapToAttribute<object>>(),
                GeneratorTestHelper.FromType<Pragmatic.Specification.Specification<object>>(),
                GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
                GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
                GeneratorTestHelper.FromTypeAssembly(
                    typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)),
                GeneratorTestHelper.FromType<Microsoft.Extensions.Logging.ILogger>(),
            ]);
}
