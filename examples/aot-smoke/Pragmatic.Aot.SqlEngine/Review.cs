using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Aot.SqlEngine;

/// <summary>A guest's review: owned, the published ones only.</summary>
[Entity]
[HasOwner]
[VisibleWhen<PublishedReviews>]
public partial class Review : IEntity
{
    public string Text { get; set; } = "";

    public bool Published { get; set; }
}
