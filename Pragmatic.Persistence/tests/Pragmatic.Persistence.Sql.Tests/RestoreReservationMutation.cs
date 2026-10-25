using Pragmatic.Actions.Mutation;

namespace PragmaticEntities.Tests;

/// <summary>A restore of an owned row: the soft delete filter is lifted, the owner's is not.</summary>
[Mutation(Mode = MutationMode.Restore)]
public partial class RestoreReservationMutation : Mutation<Reservation>
{
    public required Guid Id { get; init; }
}
