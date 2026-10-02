namespace Invoicing.Registry.Customers.Mutations;

/// <summary>
///     The accountant corrects a customer: a new address, another language, different payment terms.
/// </summary>
/// <remarks>
///     What is already on an issued invoice does not move with this: the invoice froze its own copy at
///     issue, which is the reason the two modules exist.
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[RequirePermission(RegistryPermissions.Customer.Update)]
[Endpoint(HttpVerb.Put, "api/customers/{id}")]
[ReturnsDto<CustomerDto>]
public partial class UpdateCustomerMutation : Mutation<Customer>
{
    public required Guid Id { get; init; }

    [MaxLength(200)]
    public string? Name { get; init; }

    [MaxLength(20)]
    public string? VatNumber { get; init; }

    [Email]
    [MaxLength(320)]
    public string? Email { get; init; }

    [OneOf("en-US", "it-IT")]
    public string? PreferredCulture { get; init; }

    [Range(0, 180)]
    public int? PaymentTermsDays { get; init; }

    [MapIgnore]
    public string? AddressStreet { get; init; }

    [MapIgnore]
    public string? AddressPostCode { get; init; }

    [MapIgnore]
    public string? AddressCity { get; init; }

    [MapIgnore]
    [MaxLength(2)]
    public string? AddressCountry { get; init; }

    public override Task<Result<Customer, IError>> ApplyAsync(Customer entity, CancellationToken ct = default)
    {
        // An address is replaced whole or left alone: sending one line of it and keeping the other three
        // from before would produce an address nobody wrote.
        if (AddressStreet is not null || AddressPostCode is not null || AddressCity is not null || AddressCountry is not null)
            entity.SetAddress(new PostalAddress(
                AddressStreet ?? entity.Address.Street,
                AddressPostCode ?? entity.Address.PostCode,
                AddressCity ?? entity.Address.City,
                AddressCountry ?? entity.Address.Country));

        return Task.FromResult<Result<Customer, IError>>(entity);
    }
}
