namespace Invoicing.Registry.Customers.Queries;

/// <summary>One customer, by id — this company's, because the filter is not optional.</summary>
[Query<Customer, CustomerDto>(Single = true)]
[RequirePermission(RegistryPermissions.Customer.Read)]
[Endpoint(HttpVerb.Get, "api/customers/{id}")]
public partial class GetCustomerQuery
{
    [Filter(MapTo = "PersistenceId")]
    public Guid Id { get; init; }
}
