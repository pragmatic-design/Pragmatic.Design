namespace Pragmatic.Specification.Samples.Scenarios;

/// <summary>
///     Demonstrates how to test specifications using IsSatisfiedBy.
/// </summary>
public class TestingSpecificationsSample : ISample
{
    public string Name => "Testing Specifications";
    public string Description => "Using IsSatisfiedBy to test specifications";

    public void Run()
    {
        // Create test users
        var activeAdmin = new User
        {
            Id = 1,
            Name = "Test Admin",
            IsActive = true,
            IsDeleted = false,
            Role = "Admin",
            Age = 30
        };

        var inactiveUser = new User
        {
            Id = 2,
            Name = "Inactive User",
            IsActive = false,
            IsDeleted = false,
            Role = "User",
            Age = 25
        };

        var deletedAdmin = new User
        {
            Id = 3,
            Name = "Deleted Admin",
            IsActive = true,
            IsDeleted = true,
            Role = "Admin",
            Age = 35
        };

        // Define specifications
        var isActive = UserSpecs.IsActive();
        var isAdmin = UserSpecs.IsAdmin();
        var isAdult = UserSpecs.IsAdult();
        var isActiveAdmin = UserSpecs.IsActiveAdmin();

        // Test each user against specifications
        Console.WriteLine("Testing activeAdmin:");
        Console.WriteLine($"  IsActive: {isActive.IsSatisfiedBy(activeAdmin)}");
        Console.WriteLine($"  IsAdmin: {isAdmin.IsSatisfiedBy(activeAdmin)}");
        Console.WriteLine($"  IsAdult: {isAdult.IsSatisfiedBy(activeAdmin)}");
        Console.WriteLine($"  IsActiveAdmin: {isActiveAdmin.IsSatisfiedBy(activeAdmin)}");

        Console.WriteLine();
        Console.WriteLine("Testing inactiveUser:");
        Console.WriteLine($"  IsActive: {isActive.IsSatisfiedBy(inactiveUser)}");
        Console.WriteLine($"  IsAdmin: {isAdmin.IsSatisfiedBy(inactiveUser)}");
        Console.WriteLine($"  IsActiveAdmin: {isActiveAdmin.IsSatisfiedBy(inactiveUser)}");

        Console.WriteLine();
        Console.WriteLine("Testing deletedAdmin (active=true but deleted=true):");
        Console.WriteLine($"  IsActive: {isActive.IsSatisfiedBy(deletedAdmin)} (false because IsDeleted=true)");
        Console.WriteLine($"  IsAdmin: {isAdmin.IsSatisfiedBy(deletedAdmin)}");
        Console.WriteLine($"  IsActiveAdmin: {isActiveAdmin.IsSatisfiedBy(deletedAdmin)}");
    }
}