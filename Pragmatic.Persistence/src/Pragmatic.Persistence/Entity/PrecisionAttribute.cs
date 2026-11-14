namespace Pragmatic.Persistence.Entity;

/// <summary>
///     A decimal column's precision and scale: the digits it holds in all, and how many of them follow the point. Without it a
///     <see langword="decimal" /> is <c>numeric(18,2)</c>, which loses the third and fourth digit of an amount in a currency with three minor
///     units, or of a rate.
/// </summary>
/// <remarks>
///     Read by the generator, by full name, on every engine: EF Core's configuration (<c>HasPrecision</c>), the schema Pragmatic.Migrations
///     creates, and the Sql engine's. SQLite stores a decimal as <c>REAL</c> on every precision.
/// </remarks>
/// <example>
///     <code>
///     [Precision(18, 4)]
///     public decimal Rate { get; set; }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class PrecisionAttribute : Attribute
{
    /// <summary>Creates a <see cref="PrecisionAttribute" />.</summary>
    /// <param name="precision">The digits in all, 1 to 38.</param>
    /// <param name="scale">The digits after the point, 0 to <paramref name="precision" />.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either is outside its range.</exception>
    public PrecisionAttribute(int precision, int scale)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(precision, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(precision, 38);
        ArgumentOutOfRangeException.ThrowIfNegative(scale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scale, precision);
        Precision = precision;
        Scale = scale;
    }

    /// <summary>The digits in all.</summary>
    public int Precision { get; }

    /// <summary>The digits after the point.</summary>
    public int Scale { get; }
}
