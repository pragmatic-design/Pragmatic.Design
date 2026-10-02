namespace Invoicing.Registry.Dtos;

/// <summary>
///     A customer as the accountant's screens show them.
/// </summary>
[MapFrom<Customer>]
[GenerateProjection]
public partial class CustomerDto
{
    public Guid Id { get; init; }

    public string Code { get; init; } = "";

    public string Name { get; init; } = "";

    public string VatNumber { get; init; } = "";

    public string Email { get; init; } = "";

    public string PreferredCulture { get; init; } = "";

    public int PaymentTermsDays { get; init; }
}
