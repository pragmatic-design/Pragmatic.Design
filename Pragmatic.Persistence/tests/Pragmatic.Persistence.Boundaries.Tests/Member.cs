using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>A member, on the documents engine: a data subject known by their email.</summary>
[Entity]
[Pragmatic.Privacy.DataSubject(nameof(Email))]
[BelongsTo<MembersBoundary>]
public partial class Member : IEntity
{
    [Pragmatic.Privacy.PersonalData(Pragmatic.Privacy.DataCategory.Contact, Erasure = Pragmatic.Privacy.ErasureStrategy.Pseudonymize)]
    public string Email { get; set; } = "";
}
