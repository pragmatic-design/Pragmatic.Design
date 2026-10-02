namespace Invoicing.Registry.Dtos;

/// <summary>
///     What an invoice needs about the company issuing it: the header of the document, and the prefix its
///     number is built from.
/// </summary>
[MapFrom<Organization>]
[GenerateProjection]
public partial class OrganizationBillingDetailsDto
{
    public string Slug { get; init; } = "";

    public string LegalName { get; init; } = "";

    public string VatNumber { get; init; } = "";

    public string SenderEmail { get; init; } = "";

    public string InvoiceNumberPrefix { get; init; } = "";

    public PostalAddressDto Address { get; init; } = new();
}
