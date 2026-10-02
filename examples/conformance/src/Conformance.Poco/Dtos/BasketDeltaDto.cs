using Conformance.Poco.Shapes;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Mapping.Mutation;

namespace Conformance.Poco.Dtos;

/// <summary>
///     The same basket, written as an <b>addition</b> instead of as the whole state.
/// </summary>
/// <remarks>
///     <para>
///         The strategy is deduced from the shape: a <c>[MapTo&lt;T&gt;]</c> is the complete
///         representation, so a collection it carries <b>is</b> the new state — the rows it omits
///         disappear (<c>Sync</c>). <see cref="BasketDto" /> is that, and it is this cell's control.
///     </para>
///     <para>
///         ⚠️ <c>[CollectionStrategy(AddOnly)]</c> is for the case the shape cannot express: a collection
///         that by domain rule only appends, whoever sends it. Without it, sending a single item would
///         delete the others — and the difference between the two readings is all in the data that stays
///         or disappears, not in an error.
///     </para>
/// </remarks>
[MapTo<Basket>]
public partial record BasketDeltaDto
{
    public int Id { get; init; }

    [CollectionStrategy(CollectionStrategy.AddOnly)]
    public List<BasketItemDto> Items { get; init; } = [];
}
