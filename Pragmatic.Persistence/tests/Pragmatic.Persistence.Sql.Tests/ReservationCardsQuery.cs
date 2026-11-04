using Pragmatic.Actions.Mutation;
using Pragmatic.Persistence.Query.Attributes;

namespace PragmaticEntities.Tests;

/// <summary>The reservations as cards, mapped in memory with their property loaded (#505); paged, to read the page's entities and map them.</summary>
[Query<Reservation, ReservationCard>(MapInMemory = true, Paged = true)]
[EagerLoad("Property")]
public partial class ReservationCardsQuery
{
    public string? OwnerId { get; init; }
}
