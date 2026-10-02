namespace Invoicing.Registry.Customers.Queries;

/// <summary>
///     What an invoice freezes about its customer, read by the other module.
/// </summary>
/// <remarks>
///     <para>
///         <c>[Published]</c> generates the read contract — <c>IRegistryReads.GetCustomerBillingDetails</c>
///         — in Registry's own compilation, and Billing consumes it because Billing references Registry.
///         The dependency goes one way and stays acyclic; Billing never sees <c>Customer</c>.
///     </para>
///     <para>
///         No <c>[Endpoint]</c>, and that is deliberate: a read contract is not a route. The tenant filter
///         still applies, so a customer of another company is simply not there to be read.
///     </para>
/// </remarks>
[Query<Customer, CustomerBillingDetailsDto>(Single = true)]
[Published]
public partial class GetCustomerBillingDetailsQuery
{
    [Filter(MapTo = "PersistenceId")]
    public Guid CustomerId { get; init; }
}
