
namespace Pragmatic.Specification.Samples.Scenarios;

/// <summary>
///     Demonstrates building specifications dynamically based on user input.
/// </summary>
public class DynamicFilteringSample : ISample
{
    public string Name => "Dynamic Filtering";
    public string Description => "Building specifications conditionally at runtime";

    public void Run()
    {
        var users = SampleData.Users;

        // Simulate user input / filter parameters
        var searchTerm = "a";
        var roleFilter = "Admin";
        var onlyActive = true;
        var onlyAdults = false;

        Console.WriteLine("Filter parameters:");
        Console.WriteLine($"  searchTerm: '{searchTerm}'");
        Console.WriteLine($"  roleFilter: '{roleFilter}'");
        Console.WriteLine($"  onlyActive: {onlyActive}");
        Console.WriteLine($"  onlyAdults: {onlyAdults}");
        Console.WriteLine();

        // Approach 1: Manual conditional composition
        var spec = Spec<User>.True;

        if (!string.IsNullOrEmpty(searchTerm))
            spec = spec.And(UserSpecs.NameContains(searchTerm));

        if (!string.IsNullOrEmpty(roleFilter))
            spec = spec.And(UserSpecs.HasRole(roleFilter));

        if (onlyActive)
            spec = spec.And(UserSpecs.IsActive());

        if (onlyAdults)
            spec = spec.And(UserSpecs.IsAdult());

        var result1 = users.Where(spec).ToList();
        Console.WriteLine($"Manual composition result: {string.Join(", ", result1.Select(u => u.Name))}");

        // Approach 2: Using AndIf (more concise)
        var spec2 = Spec<User>.True
            .AndIf(!string.IsNullOrEmpty(searchTerm), UserSpecs.NameContains(searchTerm!))
            .AndIf(!string.IsNullOrEmpty(roleFilter), UserSpecs.HasRole(roleFilter!))
            .AndIf(onlyActive, UserSpecs.IsActive())
            .AndIf(onlyAdults, UserSpecs.IsAdult());

        var result2 = users.Where(spec2).ToList();
        Console.WriteLine($"AndIf composition result: {string.Join(", ", result2.Select(u => u.Name))}");
    }
}