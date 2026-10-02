using Pragmatic.Validation.Types;

namespace Pragmatic.Validation.Samples.Samples;

/// <summary>
///     Core validation: SG-generated Validate(), Match, ToResult, IHttpError.
/// </summary>
public static class BasicValidationSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("1. Basic Validation — SG-Generated Validate()");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowValidRequest();
        ShowInvalidRequest();
        ShowMatchPattern();
        ShowToResult();
        ShowHttpError();

        Console.WriteLine();
    }

    private static void ShowValidRequest()
    {
        Console.WriteLine("  1.1 Valid request — all rules pass");
        Console.WriteLine("  ------------------------------------");

        var valid = new CreateUserRequest
        {
            Email = "john@example.com",
            Name = "John Doe",
            Age = 30,
            PhoneNumber = "+1-555-0123"
        };

        var result = valid.Validate();
        Console.WriteLine($"    IsSuccess: {result.IsSuccess}, Issues: {result.Count}");
        Console.WriteLine();
    }

    private static void ShowInvalidRequest()
    {
        Console.WriteLine("  1.2 Invalid request — multiple failures");
        Console.WriteLine("  -----------------------------------------");

        var invalid = new CreateUserRequest
        {
            Email = "not-an-email",
            Name = "A",  // MinLength(2)
            Age = 5      // Range(18, 120)
        };

        var errors = invalid.Validate();
        Console.WriteLine($"    IsFailure: {errors.IsFailure}, Count: {errors.Count}");
        foreach (var issue in errors.Issues)
            Console.WriteLine($"      - {issue.PropertyPath}: {issue.MessageKey}");
        Console.WriteLine();
    }

    private static void ShowMatchPattern()
    {
        Console.WriteLine("  1.3 Match() — branching on result");
        Console.WriteLine("  ------------------------------------");

        var invalid = new CreateUserRequest
        {
            Email = "bad", Name = "A", Age = 5
        };

        var message = invalid.Validate().Match(
            () => "User created successfully!",
            issues => $"Cannot create user: {issues.Count} error(s)");
        Console.WriteLine($"    {message}");
        Console.WriteLine();
    }

    private static void ShowToResult()
    {
        Console.WriteLine("  1.4 ToResult() — pipeline integration");
        Console.WriteLine("  ----------------------------------------");

        var invalid = new CreateUserRequest
        {
            Email = "bad", Name = "A", Age = 5
        };

        var pipeline = invalid.Validate().ToResult();
        Console.WriteLine($"    VoidResult.IsFailure: {pipeline.IsFailure}");
        if (pipeline.IsFailure)
            Console.WriteLine($"    Error: {pipeline.Error.Code} (HTTP {pipeline.Error.StatusCode})");
        Console.WriteLine();
    }

    private static void ShowHttpError()
    {
        Console.WriteLine("  1.5 IHttpError — ASP.NET Core integration");
        Console.WriteLine("  --------------------------------------------");

        var error = ValidationError.For("Email", "validation.required");
        Console.WriteLine($"    Code: {error.Code}");
        Console.WriteLine($"    StatusCode: {error.StatusCode}");
        Console.WriteLine($"    Title: {error.Title}");
        Console.WriteLine();
    }
}
