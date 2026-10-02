using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The accessor <c>[LogicKey]</c> promises is reachable from an operation, through the contract an
///     operation is allowed to depend on.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The concrete <c>{Entity}.Repository</c> alone is not a way in. <c>IReadRepository&lt;T&gt;</c>
///         does not declare it — it cannot: the name and the parameters come from the key — and a field
///         of the concrete type is <c>PRAG0419</c> («the generator cannot tell an injected service from
///         plain state, so the field is NOT injected»), plus <c>PRAG0527</c> on an endpoint. Without a
///         member on the contract, the one member the domain key exists to provide has no declared way
///         in, and the only route that works is resolving the concrete repository from
///         <c>IServiceProvider</c> by hand — which no document suggests.
///     </para>
///     <para>
///         The gap stays invisible while applications reach for <c>GetByIdAsync</c>: a generated member
///         with no caller has nothing to fail.
///     </para>
///     <para>
///         The way in follows the framework's own idiom for exactly this problem — <c>INavigationLoader</c>
///         and <c>INavigationLinker</c> are declared where there is no EF Core and implemented by the
///         generated repository — but a domain key cannot be an interface member, because its signature
///         is different for every entity. So it is an <b>extension on the contract</b>, built from the
///         specification the generator already emits: one predicate, not a second copy of it.
///     </para>
/// </remarks>
public class TheDomainKeyHasAWayInTests
{
    private const string Source = """
        using System;
        using Pragmatic.Persistence.Entity;

        namespace Billing
        {
            [Entity]
            public partial class Invoice : IEntity
            {
                public Guid PersistenceId { get; set; }

                [LogicKey]
                public string Number { get; private set; } = "";

                public decimal Total { get; private set; }
            }
        }
        """;

    private const string CompositeKey = """
        using System;
        using Pragmatic.Persistence.Entity;

        namespace Catalog
        {
            [Entity]
            public partial class RoomType : IEntity
            {
                public Guid PersistenceId { get; set; }

                [LogicKey(Order = 0)]
                public Guid PropertyId { get; private set; }

                [LogicKey(Order = 1)]
                public string Code { get; private set; } = "";
            }
        }
        """;

    private static string TheSpecs(string source, string hint)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, [
            GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.IEntity>(),
            GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.LogicKeyAttribute>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Specification.Spec<>)),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Persistence.Repository.IReadRepository<>)),
            // The Persistence feature is gated on EF Core being referenced: without this the entity
            // gets no repository and no specifications at all, and every assertion below would be
            // about a file that does not exist.
            GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        ]);

        var specs = GeneratorTestHelper.GetGeneratedSource(result, hint);
        specs.Should().NotBeNull("the entity declares a domain key, so it has specifications");

        return specs!;
    }

    /// <summary>The way in, on the contract an action injects.</summary>
    [Fact]
    public void TheAccessor_IsAnExtensionOnTheReadContract()
    {
        var specs = TheSpecs(Source, "Invoice.Specs");

        specs.Should().Contain("GetByNumberAsync",
            "the member the domain key promises has to be callable from an operation");
        specs.Should().Contain(
            "this global::Pragmatic.Persistence.Repository.IReadRepository<global::Billing.Invoice> repository",
            "on the contract an action may depend on — a field of the concrete repository is PRAG0419");
    }

    /// <summary>
    ///     And it asks the specification, so the key has one predicate rather than two.
    /// </summary>
    /// <remarks>
    ///     Without this, the extension would be a second place where the key's columns are written out,
    ///     free to disagree with the specification and with the repository's own accessor — the shape
    ///     this repository keeps finding as «one truth, N hand-written copies».
    /// </remarks>
    [Fact]
    public void ItAsksTheSpecification_RatherThanRepeatingThePredicate()
    {
        var specs = TheSpecs(Source, "Invoice.Specs");

        specs.Should().Contain("FirstOrDefaultAsync(ByNumber(number), ct)");
        specs.Should().NotContain("e.Number == number, ct",
            "the predicate lives in ByNumber, and nowhere else");
    }

    /// <summary>A composite key names every part, in the specification and in the accessor alike.</summary>
    [Fact]
    public void ACompositeKey_TakesEveryPart()
    {
        var specs = TheSpecs(CompositeKey, "RoomType.Specs");

        specs.Should().Contain("GetByPropertyIdAndCodeAsync");
        specs.Should().Contain("ByPropertyIdAndCode(propertyId, code)");
    }

    /// <summary>The control: an entity with no domain key gets no accessor.</summary>
    /// <remarks>
    ///     Without it, "the file contains GetBy…Async" would be satisfied by emitting an accessor for
    ///     every entity — and an accessor over no key is a lookup by nothing.
    /// </remarks>
    [Fact]
    public void WithoutADomainKey_ThereIsNoAccessor()
    {
        var specs = TheSpecs("""
            using System;
            using Pragmatic.Persistence.Entity;

            namespace Billing
            {
                [Entity]
                public partial class Receipt : IEntity
                {
                    public Guid PersistenceId { get; set; }
                    public string Note { get; private set; } = "";
                }
            }
            """, "Receipt.Specs");

        specs.Should().Contain("ById", "every entity is addressable by its identifier");
        specs.Should().NotContain("GetBy", "and only a declared domain key adds a second way in");
    }
}
