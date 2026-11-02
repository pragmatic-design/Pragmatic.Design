using Pragmatic.Internationalization.Types;
using Pragmatic.Persistence.Sql.Conversion;

namespace Pragmatic.Internationalization.Engines.Sql;

/// <summary>
///     How the Sql engine stores a <see cref="LocalizedString" />: the JSON object of culture → text, in a text column, as EF Core's
///     <c>LocalizedStringValueConverter</c> stores it (<see cref="LocalizedString.ToJson" />). The generator gives it to every
///     <see cref="LocalizedString" /> property of an entity the engine persists; nothing names it by hand.
/// </summary>
public sealed class SqlLocalizedString : ISqlConverter<LocalizedString, string>
{
    /// <inheritdoc />
    public static string ToStore(LocalizedString value) => value.ToJson();

    /// <inheritdoc />
    public static LocalizedString FromStore(string value) => LocalizedString.FromJson(value);
}
