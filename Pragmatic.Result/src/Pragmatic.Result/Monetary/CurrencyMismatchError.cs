namespace Pragmatic.Result.Monetary;

/// <summary>
///     Two amounts in different currencies were combined. Maps to 422 Unprocessable Entity.
/// </summary>
/// <remarks>
///     <para>
///         The value the <c>Money</c> members in <see cref="MoneyResultExtensions" /> return where the operators
///         throw. An amount that comes from a request can be in any currency, so mixing two is the caller's
///         request being unprocessable, not a fault of the server: the operators keep throwing, for the code
///         that knows its currencies agree, and an operation over input calls these instead.
///     </para>
///     <para>
///         <see cref="Left" /> and <see cref="Right" /> are ISO 4217 codes, in the order the operation named
///         them.
///     </para>
/// </remarks>
public sealed record CurrencyMismatchError : Error
{
    /// <inheritdoc />
    public override string Code => "CURRENCY_MISMATCH";

    /// <inheritdoc />
    public override int StatusCode => 422;

    /// <inheritdoc />
    public override string Title => "Currency mismatch";

    /// <inheritdoc />
    public override string? Description => $"Amounts in {Left} and {Right} cannot be combined without a conversion.";

    /// <summary>The currency of the amount the operation started from.</summary>
    public required string Left { get; init; }

    /// <summary>The currency of the other amount.</summary>
    public required string Right { get; init; }

    /// <inheritdoc />
    public override void WriteExtensions(IDictionary<string, object?> extensions)
    {
        extensions["left"] = Left;
        extensions["right"] = Right;
    }
}
