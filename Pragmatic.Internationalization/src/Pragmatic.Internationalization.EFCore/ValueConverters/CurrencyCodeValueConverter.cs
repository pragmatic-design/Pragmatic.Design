using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.EntityFrameworkCore.ValueConverters;

/// <summary>
///     EF Core value converter for <see cref="CurrencyCode" /> to/from string (varchar(3)).
/// </summary>
public sealed class CurrencyCodeValueConverter : ValueConverter<CurrencyCode, string>
{
    /// <summary>
    ///     Creates a new instance of the converter.
    /// </summary>
    public CurrencyCodeValueConverter()
        : base(
            currency => currency.Code,
            code => FromPersisted(code))
    {
    }

    private static CurrencyCode FromPersisted(string code)
    {
        if (CurrencyCode.TryFromCode(code, out var currency))
            return currency;

        // A row holds a currency code the generated ISO 4217 table no longer contains
        // (obsolete/renamed code, or corrupted data). Surface WHICH value broke the read,
        // otherwise the whole query fails with an opaque ArgumentException on one bad row.
        throw new InvalidOperationException(
            $"Persisted currency code '{code}' is not a valid ISO 4217 code known to " +
            $"Pragmatic.Internationalization. The row's data may be corrupted or use a retired code.");
    }
}

/// <summary>
///     EF Core value converter for nullable <see cref="CurrencyCode" /> to/from string.
/// </summary>
public sealed class NullableCurrencyCodeValueConverter : ValueConverter<CurrencyCode?, string?>
{
    /// <summary>
    ///     Creates a new instance of the converter.
    /// </summary>
    public NullableCurrencyCodeValueConverter()
        : base(
            currency => currency.HasValue ? currency.Value.Code : null,
            code => FromPersisted(code))
    {
    }

    private static CurrencyCode? FromPersisted(string? code)
    {
        if (string.IsNullOrEmpty(code))
            return null;

        if (CurrencyCode.TryFromCode(code, out var currency))
            return currency;

        throw new InvalidOperationException(
            $"Persisted currency code '{code}' is not a valid ISO 4217 code known to " +
            $"Pragmatic.Internationalization. The row's data may be corrupted or use a retired code.");
    }
}