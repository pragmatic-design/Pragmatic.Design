using Pragmatic.Mapping.Samples.Dtos;
using Pragmatic.Mapping.Samples.Entities;

namespace Pragmatic.Mapping.Samples.Samples;

public static class BasicMappingSample
{
    public static void Run()
    {
        Console.WriteLine("--- Basic Mapping Sample ---");

        // Create entity
        var user = new User
        {
            Id = 1,
            Email = "john.doe@example.com",
            FirstName = "John",
            LastName = "Doe",
            MiddleName = null,
            CreatedAt = new DateTime(2024, 1, 15),
            Address = new Address
            {
                Street = "123 Main St",
                City = "New York",
                ZipCode = "10001"
            }
        };

        // Map to DTO using static method
        var dto1 = UserDto.FromEntity(user);
        Console.WriteLine($"FromEntity: Id={dto1.Id}, Email={dto1.Email}, FullName={dto1.FullName}");
        Console.WriteLine($"  City={dto1.City}, CreatedDate={dto1.CreatedDate}, MiddleName={dto1.MiddleName}");

        // Map to DTO using extension method
        var dto2 = user.ToUserDto();
        Console.WriteLine($"Extension:  Id={dto2.Id}, FullName={dto2.FullName}");

        // Collection mapping
        var users = new List<User>
        {
            user,
            new()
            {
                Id = 2, Email = "jane@example.com", FirstName = "Jane", LastName = "Smith", CreatedAt = DateTime.Now
            }
        };

        var dtos = users.ToUserDto().ToList();
        Console.WriteLine($"Collection: Mapped {dtos.Count} users");

        Console.WriteLine();
    }
}