namespace Invoicing.Registry.Dtos;

/// <summary>
///     An address on the wire: one value, as it is on the entity.
/// </summary>
/// <remarks>
///     Nested rather than flattened into four <c>Address*</c> properties on the owner: an address is one
///     value, and it travels as one. Flattening compiles too, so this shape is a modelling choice and
///     nothing else.
/// </remarks>
[MapFrom<PostalAddress>]
[GenerateProjection]
public partial class PostalAddressDto
{
    public string Street { get; init; } = "";

    public string PostCode { get; init; } = "";

    public string City { get; init; } = "";

    public string Country { get; init; } = "";
}
