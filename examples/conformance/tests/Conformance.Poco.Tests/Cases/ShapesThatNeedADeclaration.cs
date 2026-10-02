using Conformance.Poco.Dtos;
using Conformance.Poco.Shapes;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Poco.Tests.Cases;

/// <summary>
///     The two cells in which the shape is not enough and a declaration is needed: the name of an enum
///     member that does not exist, and a collection that appends instead of replacing.
/// </summary>
/// <remarks>
///     Both are measured on the <b>data that remains</b>, not on an error: that is why they live here and
///     not in a validation suite.
/// </remarks>
public class ShapesThatNeedADeclaration
{
    // ── [MapEnum] ────────────────────────────────────────────────────────────────

    /// <summary>A channel the enum does not know lands on the member that means «I do not know».</summary>
    /// <remarks>
    ///     ⚠️ Without <c>[MapEnum(OnUnknown = Default)]</c> the generator writes a parse that throws, and on
    ///     a write path an exception is a <b>500</b> for a request that was simply wrong. With the
    ///     declaration it is <c>DeliveryChannel.Unknown</c>, which exists for this.
    /// </remarks>
    [Fact]
    public void AChannelNobodyKnows_LandsOnTheDefault()
    {
        var delivery = new DeliveryDto { Reference = "sp-1", Channel = "Carrier pigeon" }.ToEntity();

        delivery.Channel.Should().Be(DeliveryChannel.Unknown);
    }

    /// <summary>
    ///     The control: a name the enum knows is mapped, and by name.
    /// </summary>
    /// <remarks>
    ///     Without it, «the unknown becomes Unknown» is also satisfied by a mapping that answers
    ///     <c>Unknown</c> to everything — that is, by one that never worked.
    /// </remarks>
    [Fact]
    public void AChannelThatExists_IsMappedByName()
    {
        var delivery = new DeliveryDto { Reference = "sp-2", Channel = "Courier" }.ToEntity();

        delivery.Channel.Should().Be(DeliveryChannel.Courier);
    }

    /// <summary>And the comparison ignores case, which is what an old client sends.</summary>
    [Fact]
    public void TheName_IsComparedIgnoringCase()
    {
        new DeliveryDto { Channel = "pickup" }.ToEntity().Channel.Should().Be(DeliveryChannel.Pickup);
    }

    // ── [MapConstructor] ─────────────────────────────────────────────────────────

    /// <summary>
    ///     The write goes through the declared constructor, and it shows in what the constructor does.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>Delivery</c> has two parameterized constructors with the <b>same score</b>: without the
    ///         attribute the first declared would win, which takes the values as they arrive. The one with
    ///         <c>[MapConstructor]</c> normalizes the reference, so «which one ran» is a question the
    ///         <b>value</b> answers and not an inspection of the generated code — the only honest way to
    ///         ask it.
    ///     </para>
    ///     <para>
    ///         ⚠️ Measured by removal: without the attribute, <b>1 red out of 18</b>, this one, on
    ///         <c>Reference</c>. Two constructors of different score would stay green without the
    ///         attribute, that is, the cell would claim something that does not happen.
    ///     </para>
    ///     <para>
    ///         ⚠️ The constructor path must convert the DTO's properties: this pair — a string to an enum —
    ///         would otherwise be <c>CS1503</c>. The case lives here because this is where the conversion
    ///         and the choice of constructor are seen to happen <em>together</em>.
    ///     </para>
    /// </remarks>
    [Fact]
    public void TheDeclaredConstructor_IsTheOneThatRuns_AndTheConversionGoesThroughIt()
    {
        var delivery = new DeliveryDto { Reference = "  sp-3  ", Channel = "Courier" }.ToEntity();

        delivery.Reference.Should().Be("SP-3",
            "the declared constructor normalizes, the one the automatic choice would take does not: "
            + "the value says which one ran");
        delivery.Channel.Should().Be(DeliveryChannel.Courier,
            "and the enum argument was converted while being passed, not assigned afterwards — "
            + "a property the constructor takes cannot be assigned afterwards");
    }

    // ── [CollectionStrategy] ─────────────────────────────────────────────────────

    /// <summary>The delta appends: what it does not name stays where it is.</summary>
    [Fact]
    public void TheDelta_AddsWithoutRemoving()
    {
        var basket = ABasketWithTwoItems();

        new BasketDeltaDto { Id = 1, Items = [new BasketItemDto { Product = "milk", Quantity = 1 }] }
            .ApplyTo(basket);

        basket.Items.Select(i => i.Product).Should().BeEquivalentTo(["bread", "wine", "milk"]);
    }

    /// <summary>
    ///     The control, and the whole reason for the declaration: the same write without it
    ///     <b>deletes</b>.
    /// </summary>
    /// <remarks>
    ///     <see cref="BasketDto" /> is a <c>[MapTo]</c> like the delta and declares nothing, so its
    ///     collection is the whole state: sending a single item leaves a single item. Two readings of the
    ///     same request, and the difference is all in the data that remains.
    /// </remarks>
    [Fact]
    public void TheSameSendWithoutTheDeclaration_Replaces()
    {
        var basket = ABasketWithTwoItems();

        new BasketDto { Id = 1, Label = "groceries", Items = [new BasketItemDto { Product = "milk", Quantity = 1 }] }
            .ApplyTo(basket);

        basket.Items.Select(i => i.Product).Should().BeEquivalentTo(["milk"]);
    }

    /// <remarks>
    ///     The ids are distinct because the sync key must be on both sides: two rows at <c>0</c> are a
    ///     <c>DuplicateMappingKeyException</c>, which is the right refusal and not what these two cells
    ///     measure.
    /// </remarks>
    private static Basket ABasketWithTwoItems() => new()
    {
        Id = 1,
        Label = "groceries",
        Items =
        [
            new BasketItem { Id = 11, Product = "bread", Quantity = 2 },
            new BasketItem { Id = 12, Product = "wine", Quantity = 1 }
        ]
    };
}
