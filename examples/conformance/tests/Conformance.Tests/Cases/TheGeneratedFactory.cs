using Conformance.Catalog.Entities;
using Conformance.Sales.Entities;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     What ends up among the parameters of <c>{Entity}.Create(...)</c>.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b><c>[Required]</c> does not decide it.</b> The rule, in <c>EntityTransform.Properties</c>,
///         is: non-nullable <b>and without a syntactic initializer</b>, excluding keys, foreign keys and the
///         audit, soft-delete and concurrency columns. The validation attribute does not appear in the
///         condition.
///     </para>
///     <para>
///         The consequence is counter-intuitive and holds for every entity in the repository: a
///         non-nullable <c>string</c> <b>must</b> have an initializer, otherwise it is CS8618 — and with
///         <c>--warnaserror</c> it does not compile. So a required string can never be in the factory.
///     </para>
///     <para>
///         These cases do not assume: they <b>compile</b> against the generated signatures. If the rule
///         changes, they do not fail — they stop compiling, which is the strongest possible assertion on a
///         signature.
///     </para>
/// </remarks>
public class TheGeneratedFactory
{
    /// <summary>
    ///     <c>Order.Reference</c> is <c>[Required]</c>, and the factory does not ask for it.
    /// </summary>
    [Fact]
    public void ARequiredString_IsNotAFactoryParameter()
    {
        var order = Order.Create();

        order.Reference.Should().BeEmpty(
            "the `= \"\"` initializer nullability imposes takes the property out of the factory: "
            + "the entity starts with the empty string, and the validator will refuse it later");
    }

    /// <summary>
    ///     The contrast that isolates the variable: same `[Required]`, different type, different outcome.
    /// </summary>
    /// <remarks>
    ///     <c>ListPrice</c> is a non-nullable <c>decimal</c> without an initializer, so it is in.
    ///     <c>Name</c> is <c>[Required]</c> with <c>= ""</c>, so it is out. The two properties sit on the
    ///     same entity: what separates them is the initializer, not the attribute.
    /// </remarks>
    [Fact]
    public void ANonNullableValue_IsAFactoryParameter()
    {
        var item = CatalogItem.Create(7.25m);

        item.ListPrice.Should().Be(7.25m, "the decimal without an initializer is a parameter");
        item.Name.Should().BeEmpty(
            "while the [Required] string next to it, with its initializer, is not");
    }

    /// <summary>The factory sets the identity, not the caller.</summary>
    [Fact]
    public void TheIdentity_ComesFromTheFactory()
    {
        var a = Order.Create();
        var b = Order.Create();

        a.Id.Should().NotBe(Guid.Empty);
        a.Id.Should().NotBe(b.Id, "it is a GUID v7 generated on every construction");
    }
}
