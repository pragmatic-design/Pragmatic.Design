namespace Invoicing.Registry.Dtos;

/// <summary>
///     What an invoice has to freeze about the customer it is issued to.
/// </summary>
/// <remarks>
///     The shape of the read contract Billing consumes, and the reason it is a DTO and not the entity: a
///     contract is a shape, never a handle on another boundary's rows. Every field here is copied onto the
///     invoice at issue, so that moving an office next month does not rewrite a document already sent.
/// </remarks>
[MapFrom<Customer>]
[GenerateProjection]
public partial class CustomerBillingDetailsDto
{
    public Guid Id { get; init; }

    public string Code { get; init; } = "";

    public string Name { get; init; } = "";

    public string VatNumber { get; init; } = "";

    public string Email { get; init; } = "";

    public string PreferredCulture { get; init; } = "";

    public int PaymentTermsDays { get; init; }

    public PostalAddressDto Address { get; init; } = new();
}
