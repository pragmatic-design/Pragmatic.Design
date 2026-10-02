using Pragmatic.Ensure;

namespace Showcase.Catalog.Properties.Mutations;

/// <summary>
/// Creates a new hotel property.
/// Demonstrates: Mutation&lt;T&gt; + [Mutation] + [Endpoint] unified pattern,
/// LocalizedString for multi-language descriptions, Ensure guards.
/// </summary>
[Mutation(Mode = MutationMode.Create)]
[Endpoint(HttpVerb.Post, "api/properties")]
[RequirePermission(CatalogPermissions.Property.Create)]
[ReturnsDto<PropertyDetailDto>]
public partial class CreatePropertyMutation : Mutation<Property>
{
    public required string Code { get; init; }
    public required string Name { get; init; }

    /// <summary>
    /// The description, in as many cultures as the caller sends: <c>{ "en": "…", "it": "…" }</c>.
    /// </summary>
    /// <remarks>
    /// Declared as the type itself, not as a hand-converted dictionary. The generated mapping writes it
    /// onto the entity, the converter binds the object from the request body, and the published schema
    /// says it takes a culture-to-value map. A hand-converted dictionary would give only one of the three.
    /// </remarks>
    public LocalizedString? Description { get; init; }

    public string? Address { get; init; }
    public required string City { get; init; }
    public required string Country { get; init; }
    public int? StarRating { get; init; }
    public string? TimeZone { get; init; }
    public TimeOnly? CheckInTime { get; init; }
    public TimeOnly? CheckOutTime { get; init; }

    /// <summary>
    /// The property's category, a [Lookup] entity: the id is stored, the navigation is resolved from
    /// the cache the preload fills at startup rather than joined at read time.
    /// </summary>
    public Guid? CategoryId { get; init; }

    public override Task<Result<Property, IError>> ApplyAsync(Property entity, CancellationToken ct = default)
    {
        // Ensure: programming contracts (not business validation — that's handled by Validation SG)
        Ensure.ThrowIfNullOrWhiteSpace(Name);
        Ensure.ThrowIfOutOfRange(StarRating ?? 1, 1, 5);

        entity.SetCode(Code);
        entity.SetName(Name);
        entity.SetCity(City);
        entity.SetCountry(Country);

        if (Address is not null)
            entity.SetAddress(Address);
        if (StarRating.HasValue)
            entity.SetStarRating(StarRating.Value);
        if (TimeZone is not null)
            entity.SetTimeZone(TimeZone);
        if (CheckInTime.HasValue)
            entity.SetCheckInTime(CheckInTime.Value);
        if (CheckOutTime.HasValue)
            entity.SetCheckOutTime(CheckOutTime.Value);

        if (CategoryId.HasValue)
            entity.SetCategoryId(CategoryId.Value);

        return Task.FromResult<Result<Property, IError>>(entity);
    }
}
