namespace Showcase.Catalog.Properties.Endpoints;

/// <summary>
/// Retrieves a single property by ID with full details.
/// Demonstrates: <see cref="PropertyDetailDto"/> — multi-language description,
/// <c>T.Property.NoDescription</c> fallback, computed <c>DisplayLabel</c>.
/// </summary>
[Endpoint(HttpVerb.Get, "/{id}")]
[EndpointGroup<PropertiesGroup>]
[ApiSummary("Get Property")]
[ApiDescription("Retrieves a property by its unique identifier with full details including LocalizedString description.")]
[ApiTags("Properties")]
[RequirePermission("catalog.property.read")]
public partial class GetPropertyEndpoint : Endpoint<PropertyDetailDto>
{
    private IReadRepository<Property> _properties = null!;

    [FromRoute]
    public Guid Id { get; set; }

    public override async Task<Result<PropertyDetailDto>> HandleAsync(CancellationToken ct = default)
    {
        var property = await _properties.GetByIdAsync(Id, ct).ConfigureAwait(false);
        if (property is null)
            return Result<PropertyDetailDto>.Failure(NotFoundError.For<Guid>("Property", Id));

        return PropertyDetailDto.FromEntity(property);
    }
}
