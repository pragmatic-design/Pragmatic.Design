namespace Pragmatic.Patch.Samples.Samples;

/// <summary>
///     Optional&lt;T&gt; tri-state: Undefined (not sent), Null (explicitly null), Value (set).
///     Core API: HasValue, IsUndefined, Value, IfPresent, Map, GetValueOrDefault.
/// </summary>
public static class OptionalApiSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("1. Optional<T> — Tri-State API");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        Console.WriteLine("  1.1 Three states of Optional<T>");
        Console.WriteLine("  -----------------------------------");

        var undefined = Optional<string>.Undefined;
        var explicitNull = Optional<string>.Null;
        var withValue = Optional<string>.Of("hello");

        Console.WriteLine($"    Undefined:    HasValue={undefined.HasValue}, IsUndefined={undefined.IsUndefined}");
        Console.WriteLine($"    Null:         HasValue={explicitNull.HasValue}, IsUndefined={explicitNull.IsUndefined}, Value={(explicitNull.Value is null ? "(null)" : explicitNull.Value)}");
        Console.WriteLine($"    Value:        HasValue={withValue.HasValue}, Value=\"{withValue.Value}\"");
        Console.WriteLine();

        Console.WriteLine("  1.2 Implicit conversion");
        Console.WriteLine("  --------------------------");

        Optional<int> fromInt = 42;
        Optional<string> fromString = "world";
        Console.WriteLine($"    Optional<int> from 42: HasValue={fromInt.HasValue}, Value={fromInt.Value}");
        Console.WriteLine($"    Optional<string> from \"world\": Value=\"{fromString.Value}\"");
        Console.WriteLine();

        Console.WriteLine("  1.3 IfPresent — execute side effect only if value was sent");
        Console.WriteLine("  -------------------------------------------------------------");

        var changes = new List<string>();
        Optional<string>.Of("new-email@test.com").IfPresent(v => changes.Add($"Email → {v}"));
        Optional<string>.Undefined.IfPresent(v => changes.Add($"Phone → {v}")); // Skipped!
        Optional<string>.Null.IfPresent(v => changes.Add($"Notes → {v ?? "(cleared)"}")); // Runs!

        Console.WriteLine($"    Of(\"new-email\").IfPresent → executed");
        Console.WriteLine($"    Undefined.IfPresent → skipped");
        Console.WriteLine($"    Null.IfPresent → executed (clears the field)");
        Console.WriteLine($"    Changes: [{string.Join(", ", changes)}]");
        Console.WriteLine();

        Console.WriteLine("  1.4 Map — transform value if present");
        Console.WriteLine("  ----------------------------------------");

        var price = Optional<decimal>.Of(99.99m);
        var formatted = price.Map(p => $"${p:F2}");
        var missing = Optional<decimal>.Undefined.Map(p => $"${p:F2}");

        Console.WriteLine($"    Of(99.99).Map(format) → HasValue={formatted.HasValue}, Value=\"{formatted.Value}\"");
        Console.WriteLine($"    Undefined.Map(format) → HasValue={missing.HasValue}, IsUndefined={missing.IsUndefined}");
        Console.WriteLine();

        Console.WriteLine("  1.5 GetValueOrDefault — safe fallback");
        Console.WriteLine("  ----------------------------------------");

        Console.WriteLine($"    Of(42).GetValueOrDefault(0) = {Optional<int>.Of(42).GetValueOrDefault(0)}");
        Console.WriteLine($"    Undefined.GetValueOrDefault(0) = {Optional<int>.Undefined.GetValueOrDefault(0)}");
        Console.WriteLine($"    Null.GetValueOrDefault(0) = {Optional<int?>.Null.GetValueOrDefault(0)}");
        Console.WriteLine();
    }
}
