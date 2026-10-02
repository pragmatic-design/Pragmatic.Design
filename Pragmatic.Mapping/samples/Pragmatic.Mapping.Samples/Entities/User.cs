namespace Pragmatic.Mapping.Samples.Entities;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? MiddleName { get; set; }
    public DateTime CreatedAt { get; set; }
    public Address? Address { get; set; }
}

public class Address
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string ZipCode { get; set; } = "";
}