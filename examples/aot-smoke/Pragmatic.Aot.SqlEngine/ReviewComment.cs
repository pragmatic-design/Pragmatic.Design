using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Aot.SqlEngine;

/// <summary>A comment on a review, as TraitFeature generates a trait's child and marks it: visible where its review is.</summary>
[Entity]
[TraitOf<Review>]
[Relation.ManyToOne<Review>]
public partial class ReviewComment : IEntity
{
    public string Text { get; set; } = "";
}
