namespace Invoicing.Registry.Customers.Mutations;

/// <summary>
///     Puts back a customer deleted by mistake: they return to every read, with their code and their
///     history.
/// </summary>
/// <remarks>
///     The accountant's own permission to correct the register — the same one that updates a customer —
///     because undoing one's own mistake is not a privilege of its own.
/// </remarks>
[Mutation(Mode = MutationMode.Restore)]
[RequirePermission(RegistryPermissions.Customer.Update)]
[Endpoint(HttpVerb.Post, "api/customers/{id}/restore")]
[ReturnsDto<CustomerDto>]
public partial class RestoreCustomerMutation : Mutation<Customer>
{
    public required Guid Id { get; init; }
}
