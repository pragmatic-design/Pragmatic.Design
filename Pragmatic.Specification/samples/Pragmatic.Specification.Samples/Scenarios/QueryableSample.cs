
namespace Pragmatic.Specification.Samples.Scenarios;

/// <summary>
///     Demonstrates using specifications with IQueryable (simulating EF Core usage).
/// </summary>
public class QueryableSample : ISample
{
    public string Name => "IQueryable Usage";
    public string Description => "Using specifications with IQueryable (EF Core pattern)";

    public void Run()
    {
        // In real code, this would be DbContext.Users
        var queryable = SampleData.Users.AsQueryable();

        Console.WriteLine("Using specifications with IQueryable:");
        Console.WriteLine();

        // Where
        var activeAdmins = queryable.Where(UserSpecs.IsActiveAdmin()).ToList();
        Console.WriteLine($"Where(IsActiveAdmin): {string.Join(", ", activeAdmins.Select(u => u.Name))}");

        // Any
        var hasActiveAdmin = queryable.Any(UserSpecs.IsActiveAdmin());
        Console.WriteLine($"Any(IsActiveAdmin): {hasActiveAdmin}");

        // All
        var allActive = queryable.All(UserSpecs.IsActive());
        Console.WriteLine($"All(IsActive): {allActive}");

        // Count
        var adminCount = queryable.Count(UserSpecs.IsAdmin());
        Console.WriteLine($"Count(IsAdmin): {adminCount}");

        // FirstOrDefault
        var firstAdmin = queryable.FirstOrDefault(UserSpecs.IsAdmin());
        Console.WriteLine($"FirstOrDefault(IsAdmin): {firstAdmin?.Name ?? "null"}");

        Console.WriteLine();
        Console.WriteLine("Note: These extension methods use ToExpression() internally,");
        Console.WriteLine("which produces valid expression trees for EF Core SQL translation.");
    }
}