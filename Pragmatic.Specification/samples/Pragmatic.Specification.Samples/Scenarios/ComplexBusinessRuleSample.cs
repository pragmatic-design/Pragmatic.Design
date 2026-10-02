
namespace Pragmatic.Specification.Samples.Scenarios;

/// <summary>
///     Demonstrates a complex business rule composed from multiple specifications using operators.
/// </summary>
public class ComplexBusinessRuleSample : ISample
{
    public string Name => "Complex Business Rule";
    public string Description => "Composing multiple specifications for complex rules using & | ! operators";

    public void Run()
    {
        var users = SampleData.Users;

        Console.WriteLine("Business Rule: Find users who are:");
        Console.WriteLine("  - Active (not deleted)");
        Console.WriteLine("  - Administrators");
        Console.WriteLine("  - Adults (18+)");
        Console.WriteLine("  - In tenant 'tenant-1'");
        Console.WriteLine("  - Have Premium subscription");
        Console.WriteLine();

        // Define base specifications
        var isActive = UserSpecs.IsActive();
        var isAdmin = UserSpecs.IsAdmin();
        var isAdult = UserSpecs.IsAdult();
        var inTenant1 = UserSpecs.InTenant("tenant-1");
        var isPremium = UserSpecs.IsPremium();

        // Build the complex specification using operators (preferred)
        var complexSpec = isActive & isAdmin & isAdult & inTenant1 & isPremium;

        var matchingUsers = users.Where(complexSpec).ToList();

        Console.WriteLine($"Users matching all criteria: {matchingUsers.Count}");
        foreach (var user in matchingUsers)
            Console.WriteLine($"  {user}");

        // Show why others don't match
        Console.WriteLine();
        Console.WriteLine("Why others don't match:");

        // Use NOT operator for inverse matching
        var nonMatching = users.Where(!complexSpec).ToList();
        foreach (var user in nonMatching)
        {
            var reasons = new List<string>();

            if (!isActive.IsSatisfiedBy(user))
                reasons.Add("not active/deleted");
            if (!isAdmin.IsSatisfiedBy(user))
                reasons.Add("not admin");
            if (!isAdult.IsSatisfiedBy(user))
                reasons.Add("not adult");
            if (!inTenant1.IsSatisfiedBy(user))
                reasons.Add("wrong tenant");
            if (!isPremium.IsSatisfiedBy(user))
                reasons.Add("not premium");

            Console.WriteLine($"  {user.Name}: {string.Join(", ", reasons)}");
        }

        // Alternative business rule with OR
        Console.WriteLine();
        Console.WriteLine("Alternative Rule: Premium users OR Admin users in tenant-1");

        var alternativeSpec = isPremium | (isAdmin & inTenant1);
        var alternativeMatches = users.Where(alternativeSpec).ToList();

        Console.WriteLine($"Matching users: {alternativeMatches.Count}");
        foreach (var user in alternativeMatches)
            Console.WriteLine($"  {user.Name}");
    }
}
