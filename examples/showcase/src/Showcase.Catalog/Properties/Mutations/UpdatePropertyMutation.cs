namespace Showcase.Catalog.Properties.Mutations;

/// <summary>
/// Updates a hotel property.
/// Demonstrates: Mutation&lt;T&gt; + [Mutation] + [Endpoint] + [InvalidatesCache] +
/// LocalizedString for multi-language description updates.
/// </summary>
[Mutation(Mode = MutationMode.Update)]
[Endpoint(HttpVerb.Put, "api/properties/{id}")]
[InvalidatesCache("properties")]
// A visibility rule hides rows from writes too: an update loads through the same filtered
// query, so without this a deactivated property could never be reached again — not to change
// it, not to reactivate it. Managing the catalogue is what [RequirePermission] below grants.
[WithoutFilter<ActiveOnly>]
[RequirePermission(CatalogPermissions.Property.Update)]
public partial class UpdatePropertyMutation : Mutation<Property>
{
    public required Guid Id { get; init; }
    public string? Name { get; init; }

    /// <summary>
    /// The description to set, in as many cultures as the caller sends. Replaces all existing
    /// translations: <c>{ "en": "A beautiful hotel", "it": "Un bell'albergo" }</c>.
    /// </summary>
    /// <remarks>
    /// Declared as the type itself, not as a <c>Dictionary&lt;string, string&gt;</c> converted by hand
    /// in the body: the type can be declared here, and the generated mapping writes it.
    /// </remarks>
    public LocalizedString? Description { get; init; }

    public string? Address { get; init; }
    public string? City { get; init; }
    public string? Country { get; init; }
    public int? StarRating { get; init; }
    public string? TimeZone { get; init; }
    public TimeOnly? CheckInTime { get; init; }
    public TimeOnly? CheckOutTime { get; init; }
    public bool? IsActive { get; init; }

    public override Task<Result<Property, IError>> ApplyAsync(Property entity, CancellationToken ct = default)
    {
        if (Name is not null)
            entity.SetName(Name);
        if (Address is not null)
            entity.SetAddress(Address);
        if (City is not null)
            entity.SetCity(City);
        if (Country is not null)
            entity.SetCountry(Country);
        if (StarRating.HasValue)
            entity.SetStarRating(StarRating.Value);
        if (TimeZone is not null)
            entity.SetTimeZone(TimeZone);
        if (CheckInTime.HasValue)
            entity.SetCheckInTime(CheckInTime.Value);
        if (CheckOutTime.HasValue)
            entity.SetCheckOutTime(CheckOutTime.Value);
        if (IsActive.HasValue)
            entity.SetIsActive(IsActive.Value);

        // LocalizedString: partial update — only replaces when provided

        return Task.FromResult<Result<Property, IError>>(entity);
    }
}
