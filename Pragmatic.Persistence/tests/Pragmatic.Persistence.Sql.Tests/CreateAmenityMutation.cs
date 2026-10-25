using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Mutation;

namespace PragmaticEntities.Tests;

/// <summary>A create: the invoker adds the entity and saves it through the unit of work.</summary>
[Mutation(Mode = MutationMode.Create)]
public partial class CreateAmenityMutation : Mutation<Amenity>
{
    public required string Name { get; init; }
}
