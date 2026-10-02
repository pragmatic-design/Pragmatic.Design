
namespace Pragmatic.Specification.Samples.Scenarios;

/// <summary>
///     Demonstrates specification composition using operators (preferred) and methods.
/// </summary>
public class CompositionSample : ISample
{
    public string Name => "Composition";
    public string Description => "Combining specifications with & | ! operators (preferred style)";

    public void Run()
    {
        var users = SampleData.Users;

        // Define base specifications
        var isActive = UserSpecs.IsActive();
        var isAdmin = UserSpecs.IsAdmin();
        var isAdult = UserSpecs.IsAdult();
        var isDeleted = UserSpecs.IsDeleted();

        // =====================================================
        // PREFERRED: Use operators & | !
        // =====================================================

        Console.WriteLine("=== PREFERRED: Using operators & | ! ===");
        Console.WriteLine();

        // AND with &
        var activeAdmins = isActive & isAdmin;
        var result1 = users.Where(activeAdmins).ToList();
        Console.WriteLine($"Active AND Admin (isActive & isAdmin):");
        Console.WriteLine($"  {string.Join(", ", result1.Select(u => u.Name))}");
        Console.WriteLine();

        // OR with |
        var activeOrAdmin = isActive | isAdmin;
        var result2 = users.Where(activeOrAdmin).ToList();
        Console.WriteLine($"Active OR Admin (isActive | isAdmin):");
        Console.WriteLine($"  {string.Join(", ", result2.Select(u => u.Name))}");
        Console.WriteLine();

        // NOT with !
        var notDeleted = !isDeleted;
        var result3 = users.Where(notDeleted).ToList();
        Console.WriteLine($"NOT Deleted (!isDeleted):");
        Console.WriteLine($"  {string.Join(", ", result3.Select(u => u.Name))}");
        Console.WriteLine();

        // Complex composition - reads like natural boolean logic
        var complexSpec = (isActive & isAdmin) | (isAdult & !isDeleted);
        var result4 = users.Where(complexSpec).ToList();
        Console.WriteLine($"(Active AND Admin) OR (Adult AND NOT Deleted):");
        Console.WriteLine($"  {string.Join(", ", result4.Select(u => u.Name))}");
        Console.WriteLine();

        // =====================================================
        // ALTERNATIVE: Methods for conditional composition
        // =====================================================

        Console.WriteLine("=== Methods: Only for conditional composition ===");
        Console.WriteLine();

        // Use .AndIf() / .OrIf() when conditions are dynamic
        var filterByAdmin = true;
        var filterByAdult = false;

        var dynamicSpec = isActive
            .AndIf(filterByAdmin, isAdmin)
            .AndIf(filterByAdult, isAdult);

        var result5 = users.Where(dynamicSpec).ToList();
        Console.WriteLine($"Dynamic: Active .AndIf(true, Admin) .AndIf(false, Adult):");
        Console.WriteLine($"  {string.Join(", ", result5.Select(u => u.Name))}");
    }
}
