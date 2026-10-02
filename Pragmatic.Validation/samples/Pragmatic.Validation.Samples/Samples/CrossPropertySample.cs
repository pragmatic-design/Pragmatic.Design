using Pragmatic.Validation.Types;

namespace Pragmatic.Validation.Samples.Samples;

/// <summary>
///     Cross-property validation: [EqualTo], [RequiredIf], [GreaterThanProperty].
/// </summary>
public static class CrossPropertySample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("2. Cross-Property Validation");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowPasswordConfirmation();
        ShowConditionalRequired();
        ShowRangeComparison();

        Console.WriteLine();
    }

    private static void ShowPasswordConfirmation()
    {
        Console.WriteLine("  2.1 [EqualTo] — Password confirmation must match");
        Console.WriteLine("  --------------------------------------------------");

        var mismatch = new RegisterAccountRequest
        {
            Email = "user@example.com",
            Password = "SecurePass123",
            ConfirmPassword = "DifferentPass" // Mismatch!
        };

        var result = mismatch.Validate();
        Console.WriteLine($"    Password=\"{mismatch.Password}\", Confirm=\"{mismatch.ConfirmPassword}\"");
        Console.WriteLine($"    IsFailure: {result.IsFailure}");
        foreach (var issue in result.Issues)
            Console.WriteLine($"      - {issue.PropertyPath}: {issue.MessageKey}");

        // Valid case
        var matching = new RegisterAccountRequest
        {
            Email = "user@example.com",
            Password = "SecurePass123",
            ConfirmPassword = "SecurePass123"
        };

        var validResult = matching.Validate();
        Console.WriteLine($"    Matching passwords → IsSuccess: {validResult.IsSuccess}");
        Console.WriteLine();
    }

    private static void ShowConditionalRequired()
    {
        Console.WriteLine("  2.2 [RequiredIf] — Shipping address required only when NeedsShipping=true");
        Console.WriteLine("  --------------------------------------------------------------------------");

        // NeedsShipping=false → shipping fields optional
        var noShipping = new RegisterAccountRequest
        {
            Email = "user@example.com",
            Password = "SecurePass123",
            ConfirmPassword = "SecurePass123",
            NeedsShipping = false,
            ShippingStreet = null,
            ShippingCity = null
        };

        var result1 = noShipping.Validate();
        Console.WriteLine($"    NeedsShipping=false, Street=null, City=null");
        Console.WriteLine($"    IsSuccess: {result1.IsSuccess}");

        // NeedsShipping=true + fields provided → valid
        var withShipping = new RegisterAccountRequest
        {
            Email = "user@example.com",
            Password = "SecurePass123",
            ConfirmPassword = "SecurePass123",
            NeedsShipping = true,
            ShippingStreet = "Via Roma 1",
            ShippingCity = "Milano"
        };

        var result2 = withShipping.Validate();
        Console.WriteLine($"    NeedsShipping=true, Street=\"Via Roma 1\", City=\"Milano\"");
        Console.WriteLine($"    IsSuccess: {result2.IsSuccess}");

        // NeedsShipping=true + empty strings → fails
        var emptyShipping = new RegisterAccountRequest
        {
            Email = "user@example.com",
            Password = "SecurePass123",
            ConfirmPassword = "SecurePass123",
            NeedsShipping = true,
            ShippingStreet = "",
            ShippingCity = ""
        };

        var result3 = emptyShipping.Validate();
        Console.WriteLine($"    NeedsShipping=true, Street=\"\", City=\"\"");
        Console.WriteLine($"    IsFailure: {result3.IsFailure}");
        foreach (var issue in result3.Issues.Where(i =>
                     i.PropertyPath is "ShippingStreet" or "ShippingCity"))
            Console.WriteLine($"      - {issue.PropertyPath}: {issue.MessageKey}");
        Console.WriteLine();
    }

    private static void ShowRangeComparison()
    {
        Console.WriteLine("  2.3 [GreaterThanProperty] — MaxAge must be greater than MinAge");
        Console.WriteLine("  ----------------------------------------------------------------");

        var invalid = new RegisterAccountRequest
        {
            Email = "user@example.com",
            Password = "SecurePass123",
            ConfirmPassword = "SecurePass123",
            MinAge = 30,
            MaxAge = 20 // Less than MinAge!
        };

        var result = invalid.Validate();
        Console.WriteLine($"    MinAge={invalid.MinAge}, MaxAge={invalid.MaxAge}");
        foreach (var issue in result.Issues.Where(i =>
                     i.PropertyPath is "MinAge" or "MaxAge"))
            Console.WriteLine($"      - {issue.PropertyPath}: {issue.MessageKey}");
        Console.WriteLine();
    }
}
