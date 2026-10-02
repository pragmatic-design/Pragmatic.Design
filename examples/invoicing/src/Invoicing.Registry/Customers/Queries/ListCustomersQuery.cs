namespace Invoicing.Registry.Customers.Queries;

/// <summary>
///     The customers of this company, a page at a time, found by whatever the accountant remembers of
///     them — part of the name, the VAT number or the code.
/// </summary>
[Query<Customer, CustomerDto>(Paged = true)]
[RequirePermission(RegistryPermissions.Customer.Read)]
[Endpoint(HttpVerb.Get, "api/customers")]
public partial class ListCustomersQuery
{
    /// <summary>
    ///     One box over three columns. <c>IgnoreCase</c> because PostgreSQL compares as written, so
    ///     "rossi" would not find "Rossi".
    /// </summary>
    [SearchAcross(nameof(Customer.Name), nameof(Customer.VatNumber), nameof(Customer.Code), IgnoreCase = true)]
    public string? Search { get; init; }

    [Sort(DefaultDirection = SortDirection.Ascending)]
    public SortDirection? NameSort { get; init; }
}
