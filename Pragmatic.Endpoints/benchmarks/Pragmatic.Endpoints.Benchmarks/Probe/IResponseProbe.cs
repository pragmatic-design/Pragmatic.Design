namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>
///     What one row of the response probe takes out of the writer, or does differently. Each implementation is a
///     struct, so the JIT compiles a writer per row with the other branches gone.
/// </summary>
/// <remarks>
///     The <c>Constant*</c> switches are subtractions: they write a constant in place of the value, so the row writes
///     a different document and says what the value costs. The <c>Raw*</c> switches are candidates: they write the
///     same bytes another way, and the benchmark refuses to time one that does not.
/// </remarks>
internal interface IResponseProbe
{
    /// <summary>A dictionary's integer key written as a constant name instead of formatted.</summary>
    static abstract bool ConstantKeys { get; }

    /// <summary>A string value written as a constant already encoded, instead of transcoded and escaped.</summary>
    static abstract bool ConstantStrings { get; }

    /// <summary>A date written as a constant already encoded, instead of formatted.</summary>
    static abstract bool ConstantDates { get; }

    /// <summary>
    ///     Consecutive number members, and arrays of numbers, formatted into one block written as a single raw
    ///     value: one writer call instead of one per member or element.
    /// </summary>
    static abstract bool RawNumbers { get; }

    /// <summary>
    ///     Everything whose bytes the encoder cannot change — names, numbers, Guids, dates, and objects and arrays
    ///     made only of those — formatted into one block written as a single raw value.
    /// </summary>
    static abstract bool RawEncoderFree { get; }
}
