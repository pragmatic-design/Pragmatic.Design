namespace Pragmatic.Validation.Samples.Samples;

/// <summary>
///     [ValidateElements] collection element validation: every element of an annotated
///     collection is validated through its own generated <c>Validate()</c>, and failures
///     are reported with indexed property paths (<c>Members[0].Email</c>, <c>Members[1].Role</c>).
/// </summary>
public static class ValidateElementsSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("13. [ValidateElements] — Per-Element Collection Validation");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowAllValid();
        ShowIndexedFailures();

        Console.WriteLine();
    }

    private static void ShowAllValid()
    {
        Console.WriteLine("  13.1 All members valid");
        Console.WriteLine("  ------------------------");

        var request = new AddTeamRequest
        {
            TeamName = "Platform",
            Members =
            [
                new TeamMemberRequest { Email = "lead@example.com", Role = "Lead" },
                new TeamMemberRequest { Email = "dev@example.com", Role = "Engineer" }
            ]
        };

        var result = request.Validate();
        Console.WriteLine($"    {request.Members.Count} members → IsSuccess: {result.IsSuccess}");
        Console.WriteLine();
    }

    private static void ShowIndexedFailures()
    {
        Console.WriteLine("  13.2 Invalid members — indexed property paths");
        Console.WriteLine("  -----------------------------------------------");

        var request = new AddTeamRequest
        {
            TeamName = "Platform",
            Members =
            [
                new TeamMemberRequest { Email = "lead@example.com", Role = "Lead" }, // valid
                new TeamMemberRequest { Email = "not-an-email", Role = "X" }          // both invalid
            ]
        };

        var result = request.Validate();
        Console.WriteLine($"    Member[1]: Email=\"not-an-email\", Role=\"X\" (too short)");
        Console.WriteLine($"    IsFailure: {result.IsFailure}, Count: {result.Count}");
        foreach (var issue in result.Issues)
            Console.WriteLine($"      - {issue.PropertyPath}: {issue.MessageKey}");

        Console.WriteLine();
        Console.WriteLine("    Each element is validated via its generated Validate(); the parent");
        Console.WriteLine("    prefixes the element index, producing paths like Members[1].Role.");
        Console.WriteLine();
    }
}
