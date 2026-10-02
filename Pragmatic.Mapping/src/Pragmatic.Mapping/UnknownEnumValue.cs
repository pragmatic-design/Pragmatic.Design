namespace Pragmatic.Mapping;

/// <summary>
///     What a mapping does with a string that names no member of the target enum.
/// </summary>
/// <remarks>
///     ⚠️ The reason this exists: <c>Enum.Parse</c> <b>throws</b>, and on the write path the string
///     usually came from a caller. An unparseable value is then an input error answered with a
///     <c>500</c> — the shape of failure that says "the server broke" about a request that was simply
///     wrong.
/// </remarks>
public enum UnknownEnumValue
{
    /// <summary>
    ///     Throw, which is what every mapping did before this existed.
    /// </summary>
    /// <remarks>
    ///     Still the default: changing it silently would turn a loud failure into a quiet one on every
    ///     existing shape. Choosing it explicitly is also a statement — "this string comes from us".
    /// </remarks>
    Throw,

    /// <summary>The enum's default member — <c>(TEnum)0</c>, named or not.</summary>
    /// <remarks>
    ///     ⚠️ Reads a wrong value as a real one. Right when the enum has a deliberate <c>Unknown = 0</c>
    ///     member; a way to lose information when it does not.
    /// </remarks>
    Default,

    /// <summary>Null, for a nullable target.</summary>
    /// <remarks>
    ///     On a non-nullable target this behaves as <see cref="Default" />: there is nowhere to put a
    ///     null, and refusing to generate would fail a shape the author already compiled.
    /// </remarks>
    Null
}
