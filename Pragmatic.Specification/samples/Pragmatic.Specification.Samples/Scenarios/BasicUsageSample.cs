
namespace Pragmatic.Specification.Samples.Scenarios;

/// <summary>
///     Demonstrates basic specification usage with simple predicates.
/// </summary>
public class BasicUsageSample : ISample
{
    public string Name => "Basic Usage";
    public string Description => "Simple specification creation and filtering";

    public void Run()
    {
        var users = SampleData.Users;

        // Simple specification from lambda
        var activeSpec = Spec<User>.Where(u => u.IsActive);
        var activeUsers = users.Where(activeSpec).ToList();
        Console.WriteLine($"Active users: {string.Join(", ", activeUsers.Select(u => u.Name))}");

        // Reusable specification from UserSpecs
        var admins = users.Where(UserSpecs.IsAdmin()).ToList();
        Console.WriteLine($"Admins: {string.Join(", ", admins.Select(u => u.Name))}");

        // Using IsActive from UserSpecs (includes !IsDeleted check)
        var activeNotDeleted = users.Where(UserSpecs.IsActive()).ToList();
        Console.WriteLine($"Active (not deleted): {string.Join(", ", activeNotDeleted.Select(u => u.Name))}");
    }
}