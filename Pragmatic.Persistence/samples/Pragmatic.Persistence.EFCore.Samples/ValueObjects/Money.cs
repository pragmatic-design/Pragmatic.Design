namespace Pragmatic.Persistence.EFCore.Samples.ValueObjects;

/// <summary>
///     Money value object demonstrating:
///     - Immutable value type
///     - Currency + Amount combination
///     - Used with MapConverter for custom serialization
/// </summary>
public readonly record struct Money(decimal Amount, string Currency = "EUR")
{
    public static Money Zero => new(0);
    public static Money EUR(decimal amount) => new(amount, "EUR");
    public static Money USD(decimal amount) => new(amount, "USD");

    public override string ToString() => $"{Currency} {Amount:F2}";

    public static Money Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Zero;

        var parts = value.Split(' ', 2);
        if (parts.Length == 2 && decimal.TryParse(parts[1], out var amount))
            return new Money(amount, parts[0]);

        if (decimal.TryParse(value, out var simpleAmount))
            return new Money(simpleAmount);

        return Zero;
    }
}
