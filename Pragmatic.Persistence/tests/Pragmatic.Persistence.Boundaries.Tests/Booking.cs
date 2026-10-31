using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>A booking, on the Sql engine: a tenant's row.</summary>
[Entity]
[BelongsTo<FrontOfficeBoundary>]
public partial class Booking : IEntity, ITenantEntity
{
    public string Guest { get; set; } = "";

    public string TenantId { get; set; } = "";
}
