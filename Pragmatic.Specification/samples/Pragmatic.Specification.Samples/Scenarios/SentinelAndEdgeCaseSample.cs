
namespace Pragmatic.Specification.Samples.Scenarios;

/// <summary>
///     Sentinel patterns (True/False), edge cases, and negation composition.
/// </summary>
public class SentinelAndEdgeCaseSample : ISample
{
    public string Name => "Sentinels & Edge Cases";
    public string Description => "Spec.True, Spec.False, double negation, empty composition";

    public void Run()
    {
        var users = SampleData.Users;

        // Spec<T>.True — identity element for AND (pass-through)
        Console.WriteLine("Spec<T>.True — matches everything (AND identity):");
        var allUsers = users.Where(Spec<User>.True).ToList();
        Console.WriteLine($"  Count: {allUsers.Count} (all {users.Count} users)");
        Console.WriteLine();

        // Spec<T>.False — identity element for OR (blocks everything)
        Console.WriteLine("Spec<T>.False — matches nothing (OR identity):");
        var noUsers = users.Where(Spec<User>.False).ToList();
        Console.WriteLine($"  Count: {noUsers.Count} (zero users)");
        Console.WriteLine();

        // True & X = X (identity element)
        var isActive = UserSpecs.IsActive();
        var trueAndActive = Spec<User>.True & isActive;
        var result1 = users.Where(trueAndActive).ToList();
        Console.WriteLine($"True & IsActive = IsActive:");
        Console.WriteLine($"  {string.Join(", ", result1.Select(u => u.Name))}");
        Console.WriteLine();

        // False | X = X (identity element)
        var falseOrActive = Spec<User>.False | isActive;
        var result2 = users.Where(falseOrActive).ToList();
        Console.WriteLine($"False | IsActive = IsActive:");
        Console.WriteLine($"  {string.Join(", ", result2.Select(u => u.Name))}");
        Console.WriteLine();

        // Double negation: !!X = X
        var isAdmin = UserSpecs.IsAdmin();
        var doubleNot = !!isAdmin;
        var result3 = users.Where(doubleNot).ToList();
        Console.WriteLine($"!!IsAdmin = IsAdmin (double negation):");
        Console.WriteLine($"  {string.Join(", ", result3.Select(u => u.Name))}");
        Console.WriteLine();

        // Conditional build from scratch
        Console.WriteLine("Conditional build from Spec.True:");
        var filters = BuildFilters(isActive: true, role: "Admin", minAge: null);
        var result4 = users.Where(filters).ToList();
        Console.WriteLine($"  isActive=true, role=\"Admin\", minAge=null");
        Console.WriteLine($"  {string.Join(", ", result4.Select(u => u.Name))}");
    }

    private static Specification<User> BuildFilters(bool? isActive, string? role, int? minAge)
    {
        // Start with True → accumulate AND conditions
        var spec = Spec<User>.True;

        if (isActive == true)
            spec &= UserSpecs.IsActive();

        if (role is not null)
            spec &= UserSpecs.HasRole(role);

        if (minAge is not null)
            spec &= UserSpecs.IsAdult(); // Simplified

        return spec;
    }
}
