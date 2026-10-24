using Pragmatic.Persistence.Entity;

namespace PragmaticDocumentSamples.Entities;

/// <summary>A comment on a review, declared as a trait's child is: visible where its review is.</summary>
[Entity]
[TraitOf<Review>]
[Relation.ManyToOne<Review>]
public partial class ReviewComment : IEntity
{
    public string Text { get; set; } = "";
}
