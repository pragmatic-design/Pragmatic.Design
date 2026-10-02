using Pragmatic.Mapping.Attributes;
using Pragmatic.Mapping.Samples.Entities;

namespace Pragmatic.Mapping.Samples.Dtos;

[MapFrom<User>]
public partial record UserDto
{
    public int Id { get; init; }
    public string Email { get; init; } = "";

    // Concatenation: FirstName + LastName -> FullName
    [MapProperty(nameof(User.FirstName), nameof(User.LastName))]
    public string FullName { get; init; } = "";

    // Flattening: Address.City -> City
    [MapProperty("Address.City")] public string? City { get; init; }

    // Format
    [MapProperty(nameof(User.CreatedAt), Format = "yyyy-MM-dd")]
    public string CreatedDate { get; init; } = "";

    // Default value for nullable -> non-nullable
    [MapProperty(nameof(User.MiddleName), Default = "N/A")]
    public string MiddleName { get; init; } = "";
}