using Pragmatic.Validation.Attributes;
﻿using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Samples.Scenarios;

/// <summary>
///     Demonstrates the three money rules — <see cref="PositiveMoneyAttribute" />,
///     <see cref="NonNegativeMoneyAttribute" /> and <see cref="SupportedCurrencyAttribute" /> — on a
///     request the source generator validates.
/// </summary>
/// <remarks>
///     ⚠️ The type is <c>partial</c> on purpose: the generator writes <c>Validate()</c> into it at
///     compile time. There is no runtime scan of the model, and no validator to register — declaring
///     the rule is the whole wiring.
/// </remarks>
public static class ValidationAttributesSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("VALIDATION ATTRIBUTES");
        Console.WriteLine("   PositiveMoney / NonNegativeMoney / SupportedCurrency");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        var usd = CurrencyCode.FromCode("USD");

        // Valid instance: positive price, non-negative discount, supported currency.
        PrintValidation("Valid request", new PriceRequest
        {
            UnitPrice = Money.From(19.99m, usd),
            Discount = Money.From(0m, usd),
            SettlementCurrency = usd,
        });

        // Invalid instance: zero unit price, negative discount, unsupported currency.
        PrintValidation("Invalid request", new PriceRequest
        {
            UnitPrice = Money.From(0m, usd),
            Discount = Money.From(-5m, usd),
            SettlementCurrency = CurrencyCode.FromCode("JPY"),
        });

        Console.WriteLine();
    }

    private static void PrintValidation(string label, PriceRequest request)
    {
        var error = request.Validate();

        Console.WriteLine($"  {label}: {(error.IsSuccess ? "PASSED" : "FAILED")}");

        // Each issue carries a localization key, not a sentence: the words a caller reads are
        // resolved in their own culture at the serialization boundary.
        foreach (var issue in error)
            Console.WriteLine($"    - {issue.PropertyPath}: {issue.MessageKey}");
    }
}
