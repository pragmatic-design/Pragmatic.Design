using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Testing;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Samples.Scenarios;

/// <summary>
///     Demonstrates testing utilities: TestI18NScope and TestI18N.
/// </summary>
public static class TestingSupportSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("TESTING SUPPORT");
        Console.WriteLine("   TestI18NScope and TestI18N for isolated unit tests");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // TestI18NScope - Isolated Culture Context for Tests
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("TestI18NScope (Isolated Culture Context):");
        Console.WriteLine();

        // Setup initial culture
        I18NContext.SetCulture(CultureCode.EnglishUS);
        Console.WriteLine($"  Initial culture: {I18N.Culture.Code}");

        // Use TestI18NScope for isolated test
        Console.WriteLine("  Running isolated test with TestI18NScope...");
        using (new TestI18NScope(CultureCode.Italian))
        {
            Console.WriteLine($"    Inside scope: {I18N.Culture.Code}");
            Console.WriteLine($"    Currency: {I18N.Currency.Code}");

            // Test some localized behavior
            var date = new DateTime(2026, 1, 31);
            Console.WriteLine($"    Date format: {date.ToString("D", I18N.Culture.ToCultureInfo())}");
        }

        Console.WriteLine($"  After scope: {I18N.Culture.Code} (restored!)");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // TestI18N Static Factory Methods
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("TestI18N Static Factory Methods:");
        Console.WriteLine();

        Console.WriteLine("  TestI18N.WithCulture (inline scope):");
        TestI18N.WithCulture(CultureCode.German, () =>
        {
            Console.WriteLine($"    Inside: {I18N.Culture.Code}, Currency: {I18N.Currency.Code}");
        });
        Console.WriteLine($"    Outside: {I18N.Culture.Code}");
        Console.WriteLine();

        Console.WriteLine("  TestI18N.WithCulture<T> (with return value):");
        var result = TestI18N.WithCulture(CultureCode.Japanese, () =>
        {
            return $"Culture is {I18N.Culture.Code}";
        });
        Console.WriteLine($"    Result: {result}");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // Real Test Example
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("Real Test Example:");
        Console.WriteLine();
        Console.WriteLine("  [Fact]");
        Console.WriteLine("  public void MoneyFormatting_Italian_UsesComma()");
        Console.WriteLine("  {");
        Console.WriteLine("      using var _ = new TestI18NScope(CultureCode.Italian);");
        Console.WriteLine("      ");
        Console.WriteLine("      var price = Money.From(1234.56m, CurrencyCode.EUR);");
        Console.WriteLine("      var formatted = price.Format(I18N.Culture.ToCultureInfo());");
        Console.WriteLine("      ");
        Console.WriteLine("      formatted.Should().Contain(\",\"); // Italian uses comma");
        Console.WriteLine("  }");
        Console.WriteLine();

        // Actually run the test
        Console.WriteLine("  Running the test...");
        using (new TestI18NScope(CultureCode.Italian))
        {
            var price = Money.From(1234.56m, CurrencyCode.EUR);
            var formatted = price.Format(I18N.Culture.ToCultureInfo());
            var passed = formatted.Contains(",");
            Console.WriteLine($"    Formatted: {formatted}");
            Console.WriteLine($"    Test result: {(passed ? "PASSED" : "FAILED")}");
        }
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // Multiple Cultures in One Test
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("Testing Multiple Cultures:");
        Console.WriteLine();

        var cultures = new[] { CultureCode.EnglishUS, CultureCode.Italian, CultureCode.German };
        var amount = 1234.56m;

        Console.WriteLine($"  Formatting {amount} in different cultures:");
        foreach (var culture in cultures)
        {
            using (new TestI18NScope(culture))
            {
                var money = Money.From(amount, I18N.Currency);
                var formatted = money.Format(I18N.Culture.ToCultureInfo());
                Console.WriteLine($"    {culture.Code}: {formatted}");
            }
        }
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // Async Test Support
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("Async Test Support:");
        Console.WriteLine();
        Console.WriteLine("  [Fact]");
        Console.WriteLine("  public async Task AsyncTest_WithCulture_Preserved()");
        Console.WriteLine("  {");
        Console.WriteLine("      using var _ = new TestI18NScope(CultureCode.French);");
        Console.WriteLine("      ");
        Console.WriteLine("      await Task.Delay(10);");
        Console.WriteLine("      ");
        Console.WriteLine("      I18N.Culture.Should().Be(CultureCode.French);");
        Console.WriteLine("  }");
        Console.WriteLine();

        // Demonstrate async context preservation
        Console.WriteLine("  Running async test...");
        using (new TestI18NScope(CultureCode.French))
        {
            Console.WriteLine($"    Before await: {I18N.Culture.Code}");
            Task.Delay(10).Wait(); // Simulate async
            Console.WriteLine($"    After await: {I18N.Culture.Code} (preserved!)");
        }
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // Nested Scopes
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("Nested Scopes:");
        Console.WriteLine();

        using (new TestI18NScope(CultureCode.English))
        {
            Console.WriteLine($"  Outer scope: {I18N.Culture.Code}");

            using (new TestI18NScope(CultureCode.Italian))
            {
                Console.WriteLine($"    Inner scope: {I18N.Culture.Code}");
            }

            Console.WriteLine($"  Back to outer: {I18N.Culture.Code}");
        }
        Console.WriteLine();
    }
}
