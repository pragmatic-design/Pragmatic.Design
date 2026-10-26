using Pragmatic.Actions.Mutation;
using Pragmatic.Mapping.Attributes;

namespace PragmaticEntities.Tests;

/// <summary>Chooses a property's amenities by key: the keys sent are the links that remain (a many-to-many side, the Sync strategy).</summary>
[Mutation(Mode = MutationMode.Update)]
public partial class SetAmenitiesMutation : Mutation<Property>
{
    public required Guid Id { get; init; }

    [LinkIds(nameof(Property.Amenities))]
    public required List<Guid> AmenityIds { get; init; }
}
