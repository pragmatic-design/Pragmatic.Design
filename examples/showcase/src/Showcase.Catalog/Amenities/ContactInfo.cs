namespace Showcase.Catalog.Entities;

/// <summary>
/// Support contact for an amenity (email + phone). A DDD value object: immutable, compared by value,
/// persisted as an EF Core complex type (Support_Email / Support_Phone columns) via the SG [ValueObject]
/// complex-type mapping. Exercises that feature end-to-end.
/// </summary>
[ValueObject]
public partial record ContactInfo
{
    public ContactInfo(string email, string phone)
    {
        Email = email;
        Phone = phone;
    }

    // init (not get-only) so EF Core maps the columns and can bind the constructor parameters.
    public string Email { get; init; } = "";

    public string Phone { get; init; } = "";

    private static ContactInfo Validate(string email, string phone) => new(email, phone);
}
