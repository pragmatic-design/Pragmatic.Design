namespace Invoicing.Registry.Entities;

/// <summary>
///     Where a company is, as it goes on a document: four lines, compared by value.
/// </summary>
/// <remarks>
///     A value object, not an entity: it is persisted as an EF Core complex type — its columns sit in
///     the owner's table, prefixed with the property name — so an invoice can freeze one without
///     pointing at a row that may change later.
/// </remarks>
[ValueObject]
public partial record PostalAddress
{
    public PostalAddress(string street, string postCode, string city, string country)
    {
        Street = street;
        PostCode = postCode;
        City = city;
        Country = country;
    }

    // init, not get-only: EF Core maps the columns and binds the constructor parameters back.
    [MaxLength(200)]
    public string Street { get; init; } = "";

    [MaxLength(10)]
    public string PostCode { get; init; } = "";

    [MaxLength(100)]
    public string City { get; init; } = "";

    /// <summary>The two-letter country code, as on an invoice.</summary>
    [MaxLength(2)]
    public string Country { get; init; } = "";

    // Required by the value-object generator (PRAG2701): it is where a rule over the whole address would
    // go. There is none — an address is whatever the company says it is — so this is the identity.
    private static PostalAddress Validate(string street, string postCode, string city, string country)
        => new(street, postCode, city, country);
}
