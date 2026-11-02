using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.EntityFrameworkCore.ValueConverters;

/// <summary>
///     EF Core value converter for <see cref="LocalizedString" /> to/from a JSON column.
///     Translations are persisted as a JSON object of culture → value
///     (e.g. <c>{"en":"Widget","it":"Componente"}</c>).
/// </summary>
/// <remarks>
///     <see cref="LocalizedString" /> is a reference type, so a single converter covers both
///     nullable and non-nullable properties: EF Core never invokes a value converter for
///     <see langword="null" /> values (null in → null out).
/// </remarks>
public sealed class LocalizedStringValueConverter : ValueConverter<LocalizedString, string>
{
    /// <summary>
    ///     Creates a new instance of the converter.
    /// </summary>
    /// <remarks>
    ///     The text is <see cref="LocalizedString.ToJson" />'s, which the Sql and documents engines write too: one database holds the same text
    ///     whichever persistence wrote it.
    /// </remarks>
    public LocalizedStringValueConverter()
        : base(
            localized => localized.ToJson(),
            json => LocalizedString.FromJson(json))
    {
    }
}
