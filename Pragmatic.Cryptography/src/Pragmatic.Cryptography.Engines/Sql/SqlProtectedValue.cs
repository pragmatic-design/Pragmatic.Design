using Pragmatic.Persistence.Sql.Conversion;

namespace Pragmatic.Cryptography.Engines.Sql;

/// <summary>
///     How the Sql engine stores a <see cref="ProtectedValue" />: the packed bytes, in a binary column, as EF Core's
///     <c>ProtectedValueConverter</c> stores them. The generator gives it to every <see cref="ProtectedValue" /> property of an entity the engine
///     persists; nothing names it by hand.
/// </summary>
/// <remarks>
///     It performs no cryptography, for the reason EF Core's converter does not: reading protected data has an outcome a converter cannot
///     return ("this subject was erased"), so decryption stays explicit, on <see cref="ISubjectDataProtector" />.
/// </remarks>
public sealed class SqlProtectedValue : ISqlConverter<ProtectedValue, byte[]>
{
    /// <inheritdoc />
    public static byte[] ToStore(ProtectedValue value) => value.Packed ?? [];

    /// <inheritdoc />
    public static ProtectedValue FromStore(byte[] value) => new(value);
}
