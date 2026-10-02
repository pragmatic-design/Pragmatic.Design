using Invoicing.Registry.Events;
using Pragmatic.Authoring;

namespace Invoicing.Registry.Customers.Mutations;

/// <summary>
///     The accountant adds somebody to bill.
/// </summary>
/// <remarks>
///     <para>
///         The code is not an input: <c>[GeneratedValue]</c> takes it from a database sequence when the
///         row is written, so two accountants creating a customer at the same moment cannot take the
///         same one.
///     </para>
///     <para>
///         Which is also why the announcement is worth making from here. <c>CustomerRegistered</c> carries
///         that code, and a create mutation is the one place where the value does not exist yet when the
///         operation's body ends — see the remark on the event.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Create)]
[Raises<CustomerRegistered>]
[RequirePermission(RegistryPermissions.Customer.Create)]
[Endpoint(HttpVerb.Post, "api/customers")]
[CreatedAt("/api/customers/{Id}")]
[ReturnsDto<CustomerDto>]
public partial class CreateCustomerMutation : Mutation<Customer>
{
    [Required]
    [MaxLength(200)]
    public required string Name { get; init; }

    [MaxLength(20)]
    public string VatNumber { get; init; } = "";

    [Required]
    [Email]
    [MaxLength(320)]
    public required string Email { get; init; }

    [Required]
    [OneOf("en-US", "it-IT")]
    public required string PreferredCulture { get; init; }

    [Range(0, 180)]
    public int PaymentTermsDays { get; init; } = 30;

    // [MapIgnore] on all four: they are the request's shape, not the entity's. The address is one value
    // there, and ApplyAsync is the one place that knows how to build it.
    [MapIgnore]
    public string AddressStreet { get; init; } = "";

    [MapIgnore]
    public string AddressPostCode { get; init; } = "";

    [MapIgnore]
    public string AddressCity { get; init; } = "";

    [MapIgnore]
    [MaxLength(2)]
    public string AddressCountry { get; init; } = "";

    public override Task<Result<Customer, IError>> ApplyAsync(Customer entity, CancellationToken ct = default)
    {
        // The four address inputs are one value on the entity: the mapping writes properties, and this is
        // the one place that knows they are an address.
        entity.SetAddress(new PostalAddress(AddressStreet, AddressPostCode, AddressCity, AddressCountry));

        return Task.FromResult<Result<Customer, IError>>(entity);
    }
}
