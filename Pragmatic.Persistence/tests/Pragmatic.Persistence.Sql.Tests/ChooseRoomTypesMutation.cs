using Pragmatic.Actions.Mutation;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Mapping.Mutation;

namespace PragmaticEntities.Tests;

/// <summary>Chooses a property's room types by key, adding only: a room type named moves to this property (a one-to-many, the AddOnly strategy).</summary>
[Mutation(Mode = MutationMode.Update)]
public partial class ChooseRoomTypesMutation : Mutation<Property>
{
    public required Guid Id { get; init; }

    [LinkIds(nameof(Property.RoomTypes))]
    [CollectionStrategy(CollectionStrategy.AddOnly)]
    public required List<Guid> RoomTypeIds { get; init; }
}
