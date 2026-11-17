using System.Globalization;

namespace Pragmatic.Persistence.Documents.Conversion;

/// <summary>
///     A number stored as a double, read where the model has a decimal: the decimal its shortest round-trip text spells.
/// </summary>
/// <remarks>
///     ⚠️ Not the cast. <c>(decimal)d</c> depends on the runtime the application runs on (#831): .NET 10 keeps fifteen significant
///     digits, .NET 11 keeps the exact binary value, so <c>19.99</c> reads as <c>19.989999999999998436805981328</c> there. The text is
///     the number whoever wrote the document meant, and the same on every runtime.
/// </remarks>
internal static class DecimalFromDouble
{
    public static decimal Of(double value)
        => double.IsFinite(value)
            ? decimal.Parse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture)
            // What the cast threw for these, and what a value past decimal's range still throws from the parse.
            : throw new OverflowException($"The stored double {value.ToString(CultureInfo.InvariantCulture)} is not a decimal.");
}
