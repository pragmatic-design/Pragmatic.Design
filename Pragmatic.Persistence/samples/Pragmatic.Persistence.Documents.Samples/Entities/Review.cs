using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;
using PragmaticDocumentSamples.Rules;

namespace PragmaticDocumentSamples.Entities;

/// <summary>A guest's review: a tenant's, owned, the published ones only (a fixed rule on the document).</summary>
[Entity]
[HasOwner]
[VisibleWhen<PublishedReviews>]
public partial class Review : IEntity, ITenantEntity
{
    public string Text { get; set; } = "";

    public bool Published { get; set; }

    public string TenantId { get; set; } = "";
}
